using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jev.WebUi;

/// <summary>
/// Reads the repository's own files so the UI shows the code that actually runs rather than a
/// transcription of it. Level metadata is parsed straight out of each <c>ILevelDemo</c>
/// implementation for the same reason: this project deliberately does not reference
/// <c>TenLevels.Jev</c> (that project is the only one needing registry.npmjs.org at build time),
/// and a hand-copied list of level names is a list that goes stale.
/// </summary>
public sealed partial class RepositoryContent
{
    private readonly string _root;
    private readonly Lazy<IReadOnlyList<LevelInfo>> _levels;
    private readonly Lazy<IReadOnlyList<HookSample>> _hookSamples;

    public RepositoryContent(string contentRoot)
    {
        _root = FindRepositoryRoot(AppContext.BaseDirectory) ?? FindRepositoryRoot(contentRoot) ?? contentRoot;
        _levels = new Lazy<IReadOnlyList<LevelInfo>>(LoadLevels);
        _hookSamples = new Lazy<IReadOnlyList<HookSample>>(LoadHookSamples);
    }

    public string Root => _root;

    public IReadOnlyList<LevelInfo> Levels => _levels.Value;

    public IReadOnlyList<HookSample> HookSamples => _hookSamples.Value;

    /// <summary>
    /// Whether the Copilot-backed levels can be started from here. They shell out to
    /// <c>dotnet run --project src/TenLevels.Jev</c>, which needs both the project and a
    /// <c>dotnet</c> on PATH.
    /// </summary>
    public bool CliAvailable => File.Exists(Path.Combine(_root, "src", "TenLevels.Jev", "TenLevels.Jev.csproj"));

    private IReadOnlyList<LevelInfo> LoadLevels()
    {
        string levelsDirectory = Path.Combine(_root, "src", "TenLevels.Jev", "Levels");
        if (!Directory.Exists(levelsDirectory))
        {
            return [];
        }

        var levels = new List<LevelInfo>();
        foreach (string file in Directory.EnumerateFiles(levelsDirectory, "Level*.cs"))
        {
            string text = File.ReadAllText(file);
            if (NumberPattern().Match(text) is not { Success: true } number ||
                !int.TryParse(number.Groups[1].Value, out int levelNumber))
            {
                continue;
            }

            string relative = Relative(file);
            List<SourceFile> sources = [new SourceFile(relative, "csharp", text)];
            foreach (string companion in CompanionFiles(levelNumber))
            {
                if (Read(companion) is { } companionText)
                {
                    sources.Add(new SourceFile(companion, LanguageOf(companion), companionText));
                }
            }

            levels.Add(new LevelInfo(
                levelNumber,
                Capture(NamePattern(), text) ?? Path.GetFileNameWithoutExtension(file),
                Capture(SummaryPattern(), text) ?? string.Empty,
                Capture(CopilotPattern(), text) == "true",
                // No provider or mode baked in: the UI appends whichever the presenter selected,
                // and a command line on screen that does not match the one that ran is a lie.
                $"dotnet run --project src/TenLevels.Jev -- --level {levelNumber}",
                sources));
        }

        return [.. levels.OrderBy(l => l.Number)];
    }

    /// <summary>
    /// The other files a presenter needs on screen to make a level land. Levels 5, 8 and 9 are
    /// about the gate and the hook, so the level file alone shows almost nothing.
    /// </summary>
    private static string[] CompanionFiles(int level) => level switch
    {
        5 or 9 => ["src/Jev.Core/CopilotToolGate.cs", "src/Jev.Core/RiskHeuristics.cs"],
        6 or 7 or 10 => ["src/TenLevels.Jev/Copilot/DecisionToolSet.cs", "src/TenLevels.Jev/Copilot/CopilotAgentRunner.cs"],
        8 => ["src/Jev.CopilotHook/Program.cs", ".github/hooks/jev-policy.json", "scripts/jev-hook.ps1"],
        _ => ["src/Jev.Core/Models.cs"],
    };

    private List<HookSample> LoadHookSamples()
    {
        string directory = Path.Combine(_root, "samples", "hooks");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<HookSample> samples = [];
        foreach (string file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(file);
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                JsonElement root = document.RootElement;
                samples.Add(new HookSample(
                    Path.GetFileNameWithoutExtension(file),
                    root.TryGetProperty("toolName", out JsonElement tool) ? tool.GetString() ?? "" : "",
                    root.TryGetProperty("expectedDecision", out JsonElement expected) ? expected.GetString() ?? "" : "",
                    root.TryGetProperty("note", out JsonElement note) ? note.GetString() ?? "" : "",

                    // Re-serialized indented and without the sample's own bookkeeping: the
                    // presenter reads this on a projector, the files are checked in as one long
                    // line, and `expectedDecision`/`note` are this repository's annotations
                    // rather than part of the preToolUse payload Copilot sends.
                    PayloadOf(root)));
            }
            catch (JsonException)
            {
                // A malformed sample is a repository problem, not a reason for the UI to fail to
                // load. It simply does not appear in the picker.
            }
        }

        return samples;
    }

    /// <summary>The sample minus the two keys the hook contract does not define.</summary>
    private static string PayloadOf(JsonElement sample)
    {
        var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in sample.EnumerateObject())
        {
            if (property.NameEquals("expectedDecision") || property.NameEquals("note"))
            {
                continue;
            }

            payload[property.Name] = property.Value;
        }

        return JsonSerializer.Serialize(payload, IndentedJson);
    }

    public string? Read(string relativePath)
    {
        string full = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        // The catalog is fixed and none of it comes from a request, but the check costs nothing
        // and keeps that true if a future endpoint ever does take a path.
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            return null;
        }

        return File.ReadAllText(full);
    }

    private string Relative(string full) =>
        Path.GetRelativePath(_root, full).Replace(Path.DirectorySeparatorChar, '/');

    private static string LanguageOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".json" => "json",
        ".ps1" => "powershell",
        ".sh" => "bash",
        _ => "text",
    };

    private static string? Capture(Regex pattern, string text) =>
        pattern.Match(text) is { Success: true } match ? match.Groups[1].Value.Replace("\\\"", "\"", StringComparison.Ordinal) : null;

    private static string? FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JevCopilotDemo.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [GeneratedRegex(@"public int Number => (\d+);")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"public string Name => ""((?:[^""\\]|\\.)*)"";")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"public string Summary => ""((?:[^""\\]|\\.)*)"";")]
    private static partial Regex SummaryPattern();

    [GeneratedRegex(@"public bool RequiresCopilot => (true|false);")]
    private static partial Regex CopilotPattern();
}
