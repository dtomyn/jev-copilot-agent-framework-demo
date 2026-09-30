using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Jev.WebUi;

/// <summary>
/// Level 8, run honestly: the hook is a separate process with a stdin/stdout contract, so the
/// panel starts that process rather than calling the gate in-proc and formatting the result to
/// look like hook output. The interesting failures - a non-zero exit denying every tool call, two
/// JSON objects on stdout, a deadline overrun - only exist at the process boundary.
/// </summary>
public sealed class HookRunner(RepositoryContent repository)
{
    private const int TimeoutMs = 30_000;

    private readonly string _executable = Locate();

    public bool Available => _executable.Length > 0;

    public async Task<HookResponse> RunAsync(HookRequest request, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            throw new DecisionSession.UnavailableException(
                "The hook executable was not found next to the web app. Build the solution and restart.");
        }

        string payload = string.IsNullOrWhiteSpace(request.Payload) ? "{}" : request.Payload;
        string provider = Jev.Core.SystemOneEndpoint.ProviderId(DecisionSession.ParseProvider(request.Provider));

        // 'off' is a hook-only mode with no equivalent in the levels: deterministic rules only,
        // every other mutation escalated. It is worth demonstrating, so it is accepted here even
        // though the provider selector elsewhere only offers mock and live.
        string mode = (request.Mode ?? DecisionSession.DefaultMode).Trim().ToLowerInvariant();
        if (mode is not ("off" or "mock" or "live" or "auto"))
        {
            throw new DecisionSession.UnavailableException("The hook mode must be off, mock, live, or auto.");
        }

        var startInfo = new ProcessStartInfo(_executable)
        {
            WorkingDirectory = repository.Root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("preToolUse");
        startInfo.Environment["DECISION_PROVIDER"] = provider;
        startInfo.Environment["DECISION_MODE"] = mode;

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DECISION_PROVIDER"] = provider,
            ["DECISION_MODE"] = mode,
        };

        long started = Stopwatch.GetTimestamp();
        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken);
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new DecisionSession.UnavailableException(
                $"The hook did not exit within {TimeoutMs / 1000}s. In a real session Copilot's own timeout would fire first and fail open.");
        }

        long elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        string output = stdout.ToString();
        (string? decision, string? reason, bool parsed) = ParseDecision(output);

        // The two contract rules that matter, checked and reported rather than assumed: a
        // non-zero exit denies every tool call for the rest of the session, and anything other
        // than exactly one JSON object on stdout is ignored.
        bool satisfied = process.ExitCode == 0 && parsed;
        string note = (process.ExitCode, parsed) switch
        {
            (0, true) => "Exit 0 and exactly one JSON object: Copilot applies this decision.",
            (0, false) => "Exit 0 but stdout is not a single JSON object; Copilot cannot read a decision from this.",
            _ => "Non-zero exit: Copilot fails closed and denies this and every following tool call.",
        };

        return new HookResponse(
            $"{Path.GetFileName(_executable)} preToolUse",
            environment,
            payload,
            output,
            stderr.ToString(),
            process.ExitCode,
            decision,
            reason,
            satisfied,
            note,
            elapsed);
    }

    private static (string? Decision, string? Reason, bool Parsed) ParseDecision(string stdout)
    {
        string trimmed = stdout.Trim();
        if (trimmed.Length == 0)
        {
            return (null, null, false);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null, false);
            }

            return (
                root.TryGetProperty("permissionDecision", out JsonElement decision) ? decision.GetString() : null,
                root.TryGetProperty("permissionDecisionReason", out JsonElement reason) ? reason.GetString() : null,
                true);
        }
        catch (JsonException)
        {
            return (null, null, false);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone between the check and the kill.
        }
    }

    private static string Locate()
    {
        string name = OperatingSystem.IsWindows() ? "Jev.CopilotHook.exe" : "Jev.CopilotHook";
        string candidate = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(candidate) ? candidate : string.Empty;
    }
}

/// <summary>
/// Levels 6, 7 and 10 need an authenticated GitHub Copilot runtime and take tens of seconds, so
/// the browser starts the real CLI and watches its output stream rather than waiting on a request
/// that may never come back. What the audience sees is the same terminal output the README
/// promises, produced by the same command.
/// </summary>
public sealed class LevelCliRunner(RepositoryContent repository)
{
    public async Task StreamAsync(
        int level,
        string? provider,
        string? mode,
        Func<string, string, Task> emit,
        CancellationToken cancellationToken)
    {
        string resolvedProvider = Jev.Core.SystemOneEndpoint.ProviderId(DecisionSession.ParseProvider(provider));
        string resolvedMode = DecisionSession.NormalizeMode(mode);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repository.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
                 {
                     "run", "--project", "src/TenLevels.Jev", "--",
                     "--level", level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--provider", resolvedProvider,
                     "--mode", resolvedMode,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        await emit("command", $"dotnet {string.Join(' ', startInfo.ArgumentList)}");

        using var process = new Process { StartInfo = startInfo };
        var lines = System.Threading.Channels.Channel.CreateUnbounded<(string Stream, string Text)>();

        // Both redirected streams signal end-of-file with a null Data, and the channel is only
        // completed once both have. Completing on Exited instead would race the last few lines of
        // a level's output off the screen.
        int open = 2;
        void Publish(string stream, string? data)
        {
            if (data is not null)
            {
                lines.Writer.TryWrite((stream, data));
            }
            else if (Interlocked.Decrement(ref open) == 0)
            {
                lines.Writer.TryComplete();
            }
        }

        process.OutputDataReceived += (_, e) => Publish("stdout", e.Data);
        process.ErrorDataReceived += (_, e) => Publish("stderr", e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await emit("error", $"Could not start the .NET CLI: {ex.Message}");
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await foreach ((string stream, string text) in lines.Reader.ReadAllAsync(cancellationToken))
            {
                await emit(stream, text);
            }

            await process.WaitForExitAsync(cancellationToken);
            await emit("exit", process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (OperationCanceledException)
        {
            // The browser navigated away or pressed stop. Do not leave a Copilot runtime behind.
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        }
    }
}
