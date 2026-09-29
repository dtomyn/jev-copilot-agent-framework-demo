using Jev.Core;

namespace TenLevels.Jev.Levels;

public sealed class Level01SmartIf : ILevelDemo
{
    public int Number => 1;
    public string Name => "The smart if-statement";
    public string Summary => "Use one Noul probability and ordinary C# to branch.";
    public bool RequiresCopilot => false;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        const string state = "Copilot proposes: rm -rf ./artifacts after the build.";
        var response = await jev.DecideAsync(state, new Dictionary<string, JevQuestion>
        {
            ["dangerous"] = new NoulQuestion(
                "Is this proposed coding-agent action potentially destructive?",
                new NoulCriteria("Could delete important files or make an irreversible change.", "Read-only or clearly reversible.")),
        }, cancellationToken);

        var answer = (NoulAnswer)response.Answers["dangerous"];
        LevelOutput.Answer("dangerous", answer);
        Console.WriteLine(answer.Noul >= 0.80 ? "C# policy: BLOCK" : "C# policy: continue normal checks");
    }
}
