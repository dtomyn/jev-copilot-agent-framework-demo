namespace Jev.Core;

public enum GateDecision
{
    Allow,
    Ask,
    Deny,
}

/// <summary>
/// Which layer of <see cref="CopilotToolGate.EvaluateAsync"/> produced a result.
///
/// The order of those layers is the policy, so anything that has to explain a decision (the
/// presentation UI, a transcript, a log line) must be told which one fired rather than inferring
/// it from the reason string or re-running the heuristics itself. Inferring it is how an
/// explanation silently stops matching the gate it claims to explain.
/// </summary>
public enum GateStage
{
    /// <summary>A deterministic hard-deny pattern matched. No model was consulted.</summary>
    HardDeny,

    /// <summary>Possible credential material. Escalated without forwarding it to a model.</summary>
    Sensitive,

    /// <summary>The tool cannot mutate the workspace or the host.</summary>
    ReadOnlyTool,

    /// <summary>A host-registered decision function tool, which returns a judgment and nothing else.</summary>
    DecisionTool,

    /// <summary>No provider is configured, so every remaining mutation needs a human.</summary>
    NoProvider,

    /// <summary>The bounded model classification plus the code-owned confidence thresholds.</summary>
    Model,
}

/// <param name="Decision">What the caller must do with the proposed tool call.</param>
/// <param name="Reason">One sentence, safe to show a human.</param>
/// <param name="Confidence">
/// <c>max(p)</c> on the reported answer, or <c>1.0</c> for the deterministic layers. Never the
/// provider's own <c>confidence</c> field; see <see cref="ChoiceAnswer.Confidence"/>.
/// </param>
/// <param name="Stage">Which layer decided.</param>
/// <param name="Probabilities">
/// The allow/ask/deny distribution when <see cref="GateStage.Model"/> decided, otherwise
/// <c>null</c>. Reported so an explanation can show the numbers the thresholds were applied to.
/// </param>
public sealed record GateResult(
    GateDecision Decision,
    string Reason,
    double? Confidence = null,
    GateStage Stage = GateStage.Model,
    IReadOnlyDictionary<string, double>? Probabilities = null);

public sealed class CopilotToolGate
{
    // Copilot CLI tool names that cannot mutate the workspace or the host. Anything absent from
    // this set is treated as a potential mutation, so adding a name here is a policy change.
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "view", "read", "ls", "list", "grep", "rg", "glob", "search",
        "fetch", "web_fetch", "web_search",
    };

    // The decision function tools this repository registers on the Microsoft Agent Framework agent
    // (levels 6, 7, 10). They are in-process host functions that ask the provider a bounded
    // question and return a number: they cannot touch the workspace, the shell, or the host.
    //
    // They must be listed here because the repository hook applies to *every* tool the agent
    // calls, including the ones the host registered. Without this, the hook denies the agent's own
    // decision calls and levels 6, 7, and 10 cannot run. Every provider prefix is listed because
    // DecisionToolSet names its tools after the selected provider, and the hook has no way to know
    // which provider the agent in another process was started with. Hard-deny and credential
    // checks still run first, so a decision call carrying secrets is escalated, not waved through.
    private static readonly HashSet<string> DecisionTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "jev_noul", "jev_choice", "jev_score",
        "laya_noul", "laya_choice", "laya_score",
        "decider_noul", "decider_choice", "decider_score",
    };

    private readonly IJevClient? _jev;

    public CopilotToolGate(IJevClient? jev) => _jev = jev;

    private string ProviderName => _jev?.Provider switch
    {
        DecisionProvider.Laya => "Laya",
        DecisionProvider.Decider => "Decider",
        DecisionProvider.Jev => "Jev",
        _ => "The decision provider",
    };

    public async Task<GateResult> EvaluateAsync(
        string toolName,
        string toolArguments,
        CancellationToken cancellationToken = default)
    {
        string combined = $"tool={toolName}\narguments={toolArguments}";
        if (RiskHeuristics.IsHardDenied(combined))
        {
            return new GateResult(GateDecision.Deny, "Deterministic policy matched a destructive command pattern.", 1.0, GateStage.HardDeny);
        }

        if (RiskHeuristics.LooksSensitive(combined))
        {
            return new GateResult(GateDecision.Ask, "Potential secret or credential material detected; require human approval and do not send it to a decision model.", 1.0, GateStage.Sensitive);
        }

        if (ReadOnlyTools.Contains(toolName))
        {
            return new GateResult(GateDecision.Allow, "Read-only tool fast path.", 1.0, GateStage.ReadOnlyTool);
        }

        if (DecisionTools.Contains(toolName))
        {
            return new GateResult(GateDecision.Allow, "Host-registered decision tool; it returns a judgment and cannot mutate anything.", 1.0, GateStage.DecisionTool);
        }

        if (_jev is null)
        {
            return new GateResult(GateDecision.Ask, "No live/mock decision provider is configured; require human approval.", null, GateStage.NoProvider);
        }

        var questions = new Dictionary<string, JevQuestion>
        {
            ["permission"] = new ChoiceQuestion(
                "Classify the proposed Copilot tool call under the repository safety policy.",
                new Dictionary<string, string?>
                {
                    ["allow"] = "Low-risk, repository-local, reversible development action that can proceed without additional approval.",
                    ["ask"] = "Potentially consequential or ambiguous mutation that should require explicit human approval.",
                    ["deny"] = "Clearly destructive, system-wide, credential-exposing, exfiltrating, or irreversible action that should not run.",
                }),
        };

        string state = $$"""
            GitHub Copilot is requesting a tool execution.

            Tool: {{toolName}}
            Arguments: {{toolArguments}}

            The surrounding code owns the final policy. Prefer ASK when the evidence is ambiguous.
            """;

        SystemOneResponse response = await _jev.DecideAsync(state, questions, cancellationToken);
        ChoiceAnswer answer = (ChoiceAnswer)response.Answers["permission"];
        double allow = Probability(answer, "allow");
        double deny = Probability(answer, "deny");

        // Gate on AnswerConfidence (max(p)), never on the provider's own `confidence` field. That
        // field has provider-specific semantics (Laya entropy; Jev/Decider TypeSafe-compatible
        // formulas), so a threshold tuned against one provider can silently change meaning on another.
        double confidence = answer.AnswerConfidence;

        if (deny >= 0.55 || (answer.Choice.Equals("deny", StringComparison.OrdinalIgnoreCase) && confidence >= 0.60))
        {
            return new GateResult(GateDecision.Deny, $"{ProviderName} classified the tool call as unsafe within the bounded policy.", confidence, GateStage.Model, answer.Probabilities);
        }

        if (allow >= 0.85 && confidence >= 0.75)
        {
            return new GateResult(GateDecision.Allow, $"{ProviderName} classified the tool call as low risk with high confidence.", confidence, GateStage.Model, answer.Probabilities);
        }

        return new GateResult(GateDecision.Ask, $"{ProviderName} did not clear the confidence threshold; require human approval.", confidence, GateStage.Model, answer.Probabilities);
    }

    private static double Probability(ChoiceAnswer answer, string key) =>
        answer.Probabilities.TryGetValue(key, out double value) ? value : 0;
}
