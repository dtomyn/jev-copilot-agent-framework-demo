using Jev.Core;

namespace TenLevels.Jev.Levels;

public sealed class Level03Score : ILevelDemo
{
    public int Number => 3;
    public string Name => "Ordered risk";
    public string Summary => "Use a Score when the answer is an ordered rubric rather than a category.";
    public bool RequiresCopilot => false;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        const string state = "Copilot proposes to edit three source files and add a package reference.";
        var response = await jev.DecideAsync(state, new Dictionary<string, JevQuestion>
        {
            ["risk"] = new ScoreQuestion(
                "Rate the operational risk of this coding-agent action.",
                [
                    "Read-only or trivial reversible action.",
                    "Repository-local mutation with easy rollback.",
                    "Broad change that affects dependencies, generated assets, or repository state.",
                    "Destructive, system-wide, credential-sensitive, or difficult to reverse.",
                ]),
        }, cancellationToken);

        LevelOutput.Answer("risk", response.Answers["risk"]);
    }
}
