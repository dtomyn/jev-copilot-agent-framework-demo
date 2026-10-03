using Jev.Core;

namespace TenLevels.Jev.Levels;

public interface ILevelDemo
{
    int Number { get; }
    string Name { get; }
    string Summary { get; }
    bool RequiresCopilot { get; }
    Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default);
}

public static class LevelOutput
{
    /// <summary>Display name of the provider behind a client, for prompts and level output.</summary>
    public static string ProviderName(IJevClient jev) =>
        SystemOneEndpoint.ProviderDisplayName(jev.Provider);

    public static void Header(ILevelDemo level)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Level {level.Number:00}: {level.Name} ===");
        Console.WriteLine(level.Summary);
        Console.WriteLine();
    }

    // Both confidence numbers are printed because they are not the same quantity: `confidence` is
    // provider-defined (Laya uses normalized entropy; Jev and Decider expose TypeSafe-compatible
    // formulas; Clef reports max(p) itself), while `answer` is max(p), which is what policy
    // thresholds use.
    public static void Answer(string name, JevAnswer answer)
    {
        Console.WriteLine($"{name}: {answer switch
        {
            NoulAnswer n => $"noul={n.Noul:0.00}",
            ChoiceAnswer c => $"choice={c.Choice}, confidence={c.Confidence:0.00} (answer={c.AnswerConfidence:0.00})",
            ScoreAnswer s => $"score={s.Score:0.00}, confidence={s.Confidence:0.00} (answer={s.AnswerConfidence:0.00})",
            _ => answer.ToString(),
        }}");
    }
}
