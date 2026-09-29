using Jev.Core;
using TenLevels.Jev.Levels;

ILevelDemo[] levels =
[
    new Level01SmartIf(),
    new Level02Choice(),
    new Level03Score(),
    new Level04Parallel(),
    new Level05Policy(),
    new Level06CopilotTool(),
    new Level07CopilotToolbelt(),
    new Level08Hook(),
    new Level09Escalation(),
    new Level10SelfSelecting(),
];

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("""
        Usage: dotnet run --project src/TenLevels.Jev -- [options]

          --list                  List the levels and exit.
          --level <n>             Run one level (default: 1).
          --all                   Run every level that does not need a Copilot runtime.
          --include-agent         With --all, also run the Copilot-backed levels (6, 7, 10).
          --jev <mock|live>       Decision provider (default: mock).
                                  'live' requires TYPESAFE_API_KEY.
        """);
    return 0;
}

if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
{
    foreach (ILevelDemo level in levels)
    {
        string suffix = level.RequiresCopilot ? "  [Copilot runtime]" : string.Empty;
        Console.WriteLine($"{level.Number:00}  {level.Name}{suffix}");
    }

    return 0;
}

string jevMode = (ReadOption(args, "--jev") ?? "mock").ToLowerInvariant();
IJevClient jev;
try
{
    jev = jevMode switch
    {
        "mock" => new MockJevClient(),
        "live" => JevHttpClient.FromEnvironment(),
        _ => throw new ArgumentException($"--jev must be 'mock' or 'live', not '{jevMode}'."),
    };
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

// JevHttpClient.FromEnvironment owns the HttpClient it creates; MockJevClient owns nothing.
using IDisposable? jevLifetime = jev as IDisposable;

bool runAll = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
bool includeAgent = args.Contains("--include-agent", StringComparer.OrdinalIgnoreCase);

string? levelOption = ReadOption(args, "--level");
if (levelOption is not null && !int.TryParse(levelOption, out _))
{
    Console.Error.WriteLine($"--level expects a number, not '{levelOption}'. Use --list.");
    return 2;
}

int requested = int.TryParse(levelOption, out int parsed) ? parsed : 1;

ILevelDemo[] selected = runAll
    ? [.. levels.Where(l => includeAgent || !l.RequiresCopilot)]
    : [.. levels.Where(l => l.Number == requested)];

if (selected.Length == 0)
{
    Console.Error.WriteLine($"Unknown level {requested}. Use --list.");
    return 2;
}

// Ctrl+C cancels the in-flight level rather than killing the process mid-request.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

Console.WriteLine($"Jev mode: {jevMode}");

int exitCode = 0;
foreach (ILevelDemo level in selected)
{
    if (cancellation.IsCancellationRequested)
    {
        break;
    }

    try
    {
        await level.RunAsync(jev, cancellation.Token);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Cancelled.");
        return 130;
    }
    catch (Exception ex) when (level.RequiresCopilot)
    {
        Console.Error.WriteLine(
            $"Level {level.Number} needs a working, authenticated GitHub Copilot runtime: {ex.Message}");
        exitCode = 1;
        if (!runAll)
        {
            break;
        }
    }
}

return exitCode;

static string? ReadOption(string[] arguments, string name)
{
    for (int i = 0; i < arguments.Length - 1; i++)
    {
        if (arguments[i].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return arguments[i + 1];
        }
    }

    return null;
}
