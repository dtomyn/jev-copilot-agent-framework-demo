namespace Jev.WebUi;

// Request and response shapes for the presentation UI. They exist so the browser can drive the
// same Jev.Core primitives the ten levels drive, with the level's hard-coded scenario replaced by
// whatever the presenter typed. Nothing here re-implements policy: every decision still comes out
// of CopilotToolGate, and the hook panel runs the real hook executable.

public sealed record ProviderStatus(
    string Id,
    string Name,
    bool LiveConfigured,
    string LiveHint);

public sealed record ConfigResponse(
    IReadOnlyList<ProviderStatus> Providers,
    string DefaultProvider,
    string DefaultMode,
    bool HookAvailable,
    bool CliAvailable,
    bool TutorialAvailable,
    string RepositoryRoot);

public sealed record SourceFile(string Path, string Language, string Text);

public sealed record LevelInfo(
    int Number,
    string Name,
    string Summary,
    bool RequiresCopilot,
    string Cli,
    IReadOnlyList<SourceFile> Sources)
{
    /// <summary>
    /// Presenter notes for this level, read out of <c>docs/tutorial.html</c>. Attached by the
    /// endpoint rather than by <see cref="RepositoryContent"/>, which knows nothing about the
    /// tutorial and should not have to.
    /// </summary>
    public IReadOnlyList<TutorialNote> Notes { get; init; } = [];
}

public sealed record ChoiceOption(string Key, string? Description);

/// <param name="Type">One of <c>noul</c>, <c>choice</c>, <c>score</c>.</param>
public sealed record QuestionSpec(
    string Name,
    string Type,
    string Instructions,
    string? WhenTrue = null,
    string? WhenFalse = null,
    IReadOnlyList<ChoiceOption>? Choices = null,
    IReadOnlyList<string>? Levels = null);

public sealed record DecideRequest(
    string? Provider,
    string? Mode,
    string State,
    IReadOnlyList<QuestionSpec> Questions);

public sealed record AnswerView(
    string Name,
    string Type,
    double? Noul,
    string? Choice,
    double? Score,
    double? Confidence,
    double AnswerConfidence,
    IReadOnlyDictionary<string, double>? Probabilities,
    IReadOnlyDictionary<string, string>? Legend);

public sealed record DecideResponse(
    string ProviderId,
    string ProviderName,
    string Mode,
    string Description,
    string Model,
    string? Routing,
    int InputTokens,
    int OutputTokens,
    IReadOnlyList<AnswerView> Answers,
    string RequestJson,
    long ElapsedMs);

public sealed record ToolCallSpec(string Tool, string Arguments);

public sealed record GateRequest(
    string? Provider,
    string? Mode,
    IReadOnlyList<ToolCallSpec> Calls);

public sealed record GateCallView(
    string Tool,
    string Arguments,
    string Decision,
    string Stage,
    string StageLabel,
    bool ModelConsulted,
    string Reason,
    double? Confidence,
    IReadOnlyDictionary<string, double>? Probabilities,
    long ElapsedMs);

public sealed record GateResponse(
    string ProviderId,
    string ProviderName,
    string Mode,
    string Description,
    IReadOnlyList<GateCallView> Results);

/// <param name="Tool">One of <c>noul</c>, <c>choice</c>, <c>score</c>.</param>
public sealed record ToolRequest(
    string? Provider,
    string? Mode,
    string Tool,
    string State,
    string Question,
    IReadOnlyList<string>? Options);

public sealed record ToolResponse(
    string ToolName,
    string ProviderName,
    string Mode,
    string ResultJson,
    long ElapsedMs);

public sealed record HookRequest(
    string? Provider,
    string? Mode,
    string Payload);

public sealed record HookResponse(
    string Command,
    IReadOnlyDictionary<string, string> Environment,
    string Stdin,
    string Stdout,
    string Stderr,
    int ExitCode,
    string? Decision,
    string? Reason,
    bool ContractSatisfied,
    string ContractNote,
    long ElapsedMs);

public sealed record HookSample(
    string Name,
    string ToolName,
    string ExpectedDecision,
    string Note,
    string Payload);
