using System.Diagnostics;
using System.Text.Json;
using Jev.Core;

namespace Jev.WebUi;

/// <summary>
/// The handlers behind the presentation UI. Each one is the parameterized form of what a level's
/// <c>RunAsync</c> does with a hard-coded scenario: same primitives, same gate, same hook binary,
/// with the scenario supplied by the presenter instead of a string literal.
/// </summary>
public static class DemoEndpoints
{
    private static readonly JsonSerializerOptions WireJson = new(JevJson.Options) { WriteIndented = true };

    /// <summary>
    /// Levels 1 to 4: ask one provider a set of typed questions about one state and return
    /// everything the response carries, including the two different confidence numbers and
    /// Laya's routing block.
    /// </summary>
    public static async Task<DecideResponse> DecideAsync(DecideRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.State))
        {
            throw new DecisionSession.UnavailableException("The state cannot be empty.");
        }

        if (request.Questions.Count == 0)
        {
            throw new DecisionSession.UnavailableException("At least one question is required.");
        }

        IJevClient client = DecisionSession.Create(request.Provider, request.Mode, out string providerId, out string mode);
        using IDisposable? lifetime = client as IDisposable;

        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal);
        foreach (QuestionSpec spec in request.Questions)
        {
            questions[Name(spec, questions)] = Build(spec);
        }

        long started = Stopwatch.GetTimestamp();
        SystemOneResponse response = await client.DecideAsync(request.State, questions, cancellationToken);
        long elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        return new DecideResponse(
            providerId,
            DecisionSession.DisplayName(client.Provider),
            mode,
            client.Description,
            response.Model,
            response.Routing,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            [.. response.Answers.Select(pair => View(pair.Key, pair.Value))],

            // The body that goes to POST /v1/systemone, so the wire contract is on screen next to
            // the answer. `model` is omitted here for the same reason the client omits it: a null
            // model is how a Laya request asks the router to choose.
            JsonSerializer.Serialize(new { state = request.State, questions }, WireJson),
            elapsed);
    }

    /// <summary>
    /// Levels 5 and 9: run proposed tool calls through the real <see cref="CopilotToolGate"/> and
    /// report which of its layers decided, so the demo can show that a hard deny never reached a
    /// model and an ordinary edit did.
    /// </summary>
    public static async Task<GateResponse> GateAsync(GateRequest request, CancellationToken cancellationToken)
    {
        if (request.Calls.Count == 0)
        {
            throw new DecisionSession.UnavailableException("Add at least one tool call.");
        }

        IJevClient client = DecisionSession.Create(request.Provider, request.Mode, out string providerId, out string mode);
        using IDisposable? lifetime = client as IDisposable;
        var gate = new CopilotToolGate(client);

        var results = new List<GateCallView>(request.Calls.Count);
        foreach (ToolCallSpec call in request.Calls)
        {
            string arguments = string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments;
            long started = Stopwatch.GetTimestamp();
            GateResult result = await gate.EvaluateAsync(call.Tool ?? string.Empty, arguments, cancellationToken);
            long elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            results.Add(new GateCallView(
                call.Tool ?? string.Empty,
                arguments,
                result.Decision.ToString().ToLowerInvariant(),
                result.Stage.ToString(),
                StageLabel(result.Stage),
                result.Stage == GateStage.Model,
                result.Reason,
                result.Confidence,
                result.Probabilities,
                elapsed));
        }

        return new GateResponse(
            providerId,
            DecisionSession.DisplayName(client.Provider),
            mode,
            client.Description,
            results);
    }

    /// <summary>
    /// Levels 6, 7 and 10: execute one decision function tool with the arguments the agent would
    /// have passed, and return the exact JSON string the agent receives back. The bodies mirror
    /// <c>TenLevels.Jev.Copilot.DecisionToolSet</c>, which cannot be referenced from here without
    /// dragging the Copilot runtime download into this project's build.
    /// </summary>
    public static async Task<ToolResponse> ToolAsync(ToolRequest request, CancellationToken cancellationToken)
    {
        IJevClient client = DecisionSession.Create(request.Provider, request.Mode, out _, out string mode);
        using IDisposable? lifetime = client as IDisposable;
        string prefix = client.Provider == DecisionProvider.Laya ? "laya" : "jev";
        string kind = (request.Tool ?? string.Empty).Trim().ToLowerInvariant();
        IReadOnlyList<string> options = request.Options ?? [];

        JevQuestion question = kind switch
        {
            "noul" => new NoulQuestion(request.Question),
            "choice" when options.Count >= 2 => new ChoiceQuestion(
                request.Question,
                options.ToDictionary(c => c, c => (string?)c, StringComparer.Ordinal)),
            "score" when options.Count >= 2 => new ScoreQuestion(request.Question, [.. options]),
            "choice" or "score" => throw new DecisionSession.UnavailableException(
                $"A {kind} tool call needs at least two options; the tool itself returns an error object below two."),
            _ => throw new DecisionSession.UnavailableException($"Unknown decision tool '{request.Tool}'."),
        };

        long started = Stopwatch.GetTimestamp();
        SystemOneResponse response = await client.DecideAsync(
            request.State,
            new Dictionary<string, JevQuestion> { ["answer"] = question },
            cancellationToken);
        long elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        JevAnswer answer = response.Answers["answer"];
        object payload = answer switch
        {
            NoulAnswer noul => new { type = "noul", probability = noul.Noul },
            ChoiceAnswer choice => new
            {
                type = "choice",
                choice = choice.Choice,
                confidence = choice.Confidence,
                answerConfidence = choice.AnswerConfidence,
                probabilities = choice.Probabilities,
            },
            ScoreAnswer score => new
            {
                type = "score",
                score = score.Score,
                confidence = score.Confidence,
                answerConfidence = score.AnswerConfidence,
                legend = score.Legend,
                probabilities = score.Probabilities,
            },
            _ => new { type = "unknown" },
        };

        return new ToolResponse(
            $"{prefix}_{kind}",
            DecisionSession.DisplayName(client.Provider),
            mode,
            JsonSerializer.Serialize(payload, WireJson),
            elapsed);
    }

    private static string StageLabel(GateStage stage) => stage switch
    {
        GateStage.HardDeny => "Deterministic hard deny",
        GateStage.Sensitive => "Credential guard",
        GateStage.ReadOnlyTool => "Read-only fast path",
        GateStage.DecisionTool => "Host decision tool",
        GateStage.NoProvider => "No provider configured",
        _ => "Model + code-owned thresholds",
    };

    private static string Name(QuestionSpec spec, Dictionary<string, JevQuestion> taken)
    {
        string baseName = string.IsNullOrWhiteSpace(spec.Name) ? spec.Type : spec.Name.Trim();
        if (!taken.ContainsKey(baseName))
        {
            return baseName;
        }

        int suffix = 2;
        while (taken.ContainsKey($"{baseName}_{suffix}"))
        {
            suffix++;
        }

        return $"{baseName}_{suffix}";
    }

    private static JevQuestion Build(QuestionSpec spec) => spec.Type?.Trim().ToLowerInvariant() switch
    {
        "noul" => new NoulQuestion(
            spec.Instructions,
            string.IsNullOrWhiteSpace(spec.WhenTrue) && string.IsNullOrWhiteSpace(spec.WhenFalse)
                ? null
                : new NoulCriteria(Blank(spec.WhenTrue), Blank(spec.WhenFalse))),

        "choice" => (spec.Choices ?? []).Count >= 2
            ? new ChoiceQuestion(
                spec.Instructions,
                (spec.Choices ?? []).ToDictionary(c => c.Key, c => Blank(c.Description), StringComparer.Ordinal))
            : throw new DecisionSession.UnavailableException("A choice question needs at least two options."),

        "score" => (spec.Levels ?? []).Count >= 2
            ? new ScoreQuestion(spec.Instructions, [.. (spec.Levels ?? []).Where(l => !string.IsNullOrWhiteSpace(l))])
            : throw new DecisionSession.UnavailableException("A score question needs at least two ordered levels."),

        _ => throw new DecisionSession.UnavailableException($"Unknown question type '{spec.Type}'."),
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static AnswerView View(string name, JevAnswer answer) => answer switch
    {
        NoulAnswer noul => new AnswerView(name, "noul", noul.Noul, null, null, null, noul.AnswerConfidence, null, null),
        ChoiceAnswer choice => new AnswerView(
            name, "choice", null, choice.Choice, null, choice.Confidence, choice.AnswerConfidence, choice.Probabilities, null),
        ScoreAnswer score => new AnswerView(
            name, "score", null, null, score.Score, score.Confidence, score.AnswerConfidence, score.Probabilities, score.Legend),
        _ => new AnswerView(name, "unknown", null, null, null, null, 0, null, null),
    };
}
