namespace Jev.Core;

public enum GateDecision
{
    Allow,
    Ask,
    Deny,
}

public sealed record GateResult(GateDecision Decision, string Reason, double? Confidence = null);

public sealed class CopilotToolGate
{
    // Copilot CLI tool names that cannot mutate the workspace or the host. Anything absent from
    // this set is treated as a potential mutation, so adding a name here is a policy change.
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "view", "read", "ls", "list", "grep", "rg", "glob", "search",
        "fetch", "web_fetch", "web_search",
    };

    // The Jev function tools this repository registers on the Microsoft Agent Framework agent
    // (levels 6, 7, 10). They are in-process host functions that ask Jev a bounded question and
    // return a number: they cannot touch the workspace, the shell, or the host.
    //
    // They must be listed here because the repository hook applies to *every* tool the agent
    // calls, including the ones the host registered. Without this, the hook denies the agent's own
    // Jev calls and levels 6, 7, and 10 cannot run. Hard-deny and credential checks still run
    // first, so a Jev call carrying secrets is still escalated rather than waved through.
    private static readonly HashSet<string> JevDecisionTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "jev_noul", "jev_choice", "jev_score",
    };

    private readonly IJevClient? _jev;

    public CopilotToolGate(IJevClient? jev) => _jev = jev;

    public async Task<GateResult> EvaluateAsync(
        string toolName,
        string toolArguments,
        CancellationToken cancellationToken = default)
    {
        string combined = $"tool={toolName}\narguments={toolArguments}";
        if (RiskHeuristics.IsHardDenied(combined))
        {
            return new GateResult(GateDecision.Deny, "Deterministic policy matched a destructive command pattern.", 1.0);
        }

        if (RiskHeuristics.LooksSensitive(combined))
        {
            return new GateResult(GateDecision.Ask, "Potential secret or credential material detected; require human approval and do not send it to Jev.", 1.0);
        }

        if (ReadOnlyTools.Contains(toolName))
        {
            return new GateResult(GateDecision.Allow, "Read-only tool fast path.", 1.0);
        }

        if (JevDecisionTools.Contains(toolName))
        {
            return new GateResult(GateDecision.Allow, "Host-registered Jev decision tool; it returns a judgment and cannot mutate anything.", 1.0);
        }

        if (_jev is null)
        {
            return new GateResult(GateDecision.Ask, "No live/mock Jev decision provider is configured; require human approval.");
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

        if (deny >= 0.55 || (answer.Choice.Equals("deny", StringComparison.OrdinalIgnoreCase) && answer.Confidence >= 0.60))
        {
            return new GateResult(GateDecision.Deny, "Jev classified the tool call as unsafe within the bounded policy.", answer.Confidence);
        }

        if (allow >= 0.85 && answer.Confidence >= 0.75)
        {
            return new GateResult(GateDecision.Allow, "Jev classified the tool call as low risk with high confidence.", answer.Confidence);
        }

        return new GateResult(GateDecision.Ask, "Jev did not clear the confidence threshold; require human approval.", answer.Confidence);
    }

    private static double Probability(ChoiceAnswer answer, string key) =>
        answer.Probabilities.TryGetValue(key, out double value) ? value : 0;
}
