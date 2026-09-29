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
    public static void Header(ILevelDemo level)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Level {level.Number:00}: {level.Name} ===");
        Console.WriteLine(level.Summary);
        Console.WriteLine();
    }

    public static void Answer(string name, JevAnswer answer)
    {
        Console.WriteLine($"{name}: {answer switch
        {
            NoulAnswer n => $"noul={n.Noul:0.00}",
            ChoiceAnswer c => $"choice={c.Choice}, confidence={c.Confidence:0.00}",
            ScoreAnswer s => $"score={s.Score:0.00}, confidence={s.Confidence:0.00}",
            _ => answer.ToString(),
        }}");
    }
}
