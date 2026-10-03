using System.Globalization;
using System.Text.Json;
using Jev.Core;

// GitHub Copilot CLI `preToolUse` command hook.
//
// Contract (docs.github.com/en/copilot/reference/hooks-reference):
//   stdin   { "sessionId", "timestamp", "cwd", "toolName", "toolArgs" }
//   stdout  exactly one JSON object: { "permissionDecision": "allow"|"ask"|"deny",
//                                      "permissionDecisionReason": string }
//   exit 0  -> stdout is parsed as the decision
//   exit !0 -> fail-closed, the tool call is denied
//   timeout -> fail-OPEN, Copilot falls back to its normal permission flow
//
// Because a timeout fails open, this program enforces its own deadline that is deliberately
// shorter than the hook's `timeoutSec`, so a slow decision call produces an explicit `ask`
// instead of letting the outer timeout hand the decision back to the default flow.
//
// The decision provider is selected by DECISION_PROVIDER (jev | laya | decider | clef)| laya | decider) and the mode by
// DECISION_MODE (off | mock | live | auto). The original JEV_* names remain accepted.

const int DefaultDeadlineMs = 4000;

string eventName = args.FirstOrDefault() ?? "preToolUse";
if (!eventName.Equals("preToolUse", StringComparison.OrdinalIgnoreCase))
{
    // Not our event. Emit an empty object so Copilot applies its normal flow.
    Console.Out.Write("{}\n");
    return 0;
}

using var deadline = new CancellationTokenSource(ReadDeadline());

try
{
    string input = await Console.In.ReadToEndAsync(deadline.Token);
    using JsonDocument document = JsonDocument.Parse(input);
    JsonElement root = document.RootElement;

    string toolName = NormalizeToolName(ReadString(root, "toolName", "tool_name") ?? string.Empty);
    string toolArgs = ReadValue(root, "toolArgs", "tool_input") ?? "{}";

    IJevClient? jev = CreateProviderFromEnvironment();
    using IDisposable? jevLifetime = jev as IDisposable;
    var gate = new CopilotToolGate(jev);
    GateResult result = await gate.EvaluateAsync(toolName, toolArgs, deadline.Token);

    Emit(
        result.Decision switch
        {
            GateDecision.Allow => "allow",
            GateDecision.Deny => "deny",
            _ => "ask",
        },
        result.Reason);
    return 0;
}
catch (OperationCanceledException)
{
    // Beat Copilot's own hook timeout, which would otherwise fail open.
    Console.Error.WriteLine("The policy hook exceeded its internal deadline; requiring human approval.");
    Emit("ask", "The policy hook did not reach a decision in time; require human approval.");
    return 0;
}
catch (Exception ex)
{
    // A broken decision call must not become an accidental allow, and must not become an opaque
    // crash either: a non-zero exit would deny every tool call while the provider is misconfigured.
    Console.Error.WriteLine($"The policy hook degraded to human approval: {ex.Message}");
    Emit("ask", "The policy hook could not complete safely; require human approval.");
    return 0;
}

// Exactly one JSON object on stdout. Two objects concatenate into invalid JSON and are ignored.
static void Emit(string decision, string reason) =>
    Console.Out.Write(JsonSerializer.Serialize(
        new HookDecision(decision, reason), JevJson.Options) + "\n");

static TimeSpan ReadDeadline()
{
    string? raw = ReadEnvironment("DECISION_HOOK_DEADLINE_MS", "JEV_HOOK_DEADLINE_MS");
    return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) && ms > 0
        ? TimeSpan.FromMilliseconds(ms)
        : TimeSpan.FromMilliseconds(DefaultDeadlineMs);
}

static IJevClient? CreateProviderFromEnvironment()
{
    string mode = (ReadEnvironment("DECISION_MODE", "JEV_MODE") ?? "off").Trim().ToLowerInvariant();
    DecisionProvider provider = SystemOneEndpoint.ProviderFromEnvironment();

    // The live HTTP call is capped well inside the hook's own deadline. A local Laya container is
    // fast once warm, but its first request after a cold start loads a checkpoint, which is far
    // longer than any hook budget: that turns into an explicit `ask`, never a silent allow.
    return mode switch
    {
        "off" => null,
        "mock" => new MockJevClient(provider),
        "live" => SystemOneHttpClient.FromEnvironment(provider, TimeSpan.FromSeconds(2)),
        "auto" when SystemOneEndpoint.IsLiveConfigured(provider)
            => SystemOneHttpClient.FromEnvironment(provider, TimeSpan.FromSeconds(2)),
        "auto" => null,
        _ => throw new InvalidOperationException("DECISION_MODE must be off, mock, live, or auto."),
    };
}

// DECISION_* is the current name; the JEV_* name is still honoured so an existing .env or shell
// session keeps working after the provider became selectable.
static string? ReadEnvironment(string name, string legacyName)
{
    string? value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? Environment.GetEnvironmentVariable(legacyName) : value;
}

// Copilot CLI and other agent CLIs name the same primitives differently. Normalizing here keeps
// the gate's read-only fast path meaningful across hosts.
static string NormalizeToolName(string value) => value.ToLowerInvariant() switch
{
    "bash" or "shell" or "run" => "bash",
    "read" => "view",
    "write" => "create",
    "edit" or "str_replace" or "apply_patch" => "edit",
    "grep" => "grep",
    "glob" => "glob",
    "webfetch" or "fetch" => "web_fetch",
    "websearch" => "web_search",
    "askuserquestion" => "ask_user",
    "todowrite" => "update_todo",
    "agent" or "task" => "task",
    _ => value,
};

static string? ReadString(JsonElement root, params string[] names)
{
    foreach (string name in names)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
    }

    return null;
}

static string? ReadValue(JsonElement root, params string[] names)
{
    foreach (string name in names)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            continue;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    return null;
}

internal sealed record HookDecision(string PermissionDecision, string PermissionDecisionReason);
