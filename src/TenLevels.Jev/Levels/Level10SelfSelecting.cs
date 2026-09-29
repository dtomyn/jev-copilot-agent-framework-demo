using Jev.Core;
using TenLevels.Jev.Copilot;

namespace TenLevels.Jev.Levels;

public sealed class Level10SelfSelecting : ILevelDemo
{
    public int Number => 10;
    public string Name => "The agent reaches for Jev itself";
    public string Summary => "Copilot receives a Jev skill/toolbelt and decides when a typed fast judgment is the right primitive.";
    public bool RequiresCopilot => true;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        await CopilotAgentRunner.RunAsync(
            """
            You are an experienced coding agent with optional Jev decision tools.
            Reach for Jev when the subproblem is a bounded System-One-style judgment: yes/no, one-of-N choice, or an ordered score.
            Do not use Jev for facts you can calculate or inspect deterministically, and do not delegate open-ended code generation to Jev.
            When a Jev result is uncertain, preserve that uncertainty and recommend a human check instead of pretending certainty.
            """,
            "A developer asks: 'Should I run the entire test suite for a change that only renames a public JSON property and updates its serializer mapping?' Decide for yourself whether Jev is useful. If it is, formulate a narrow typed question and call the appropriate tool; otherwise explain why not.",
            new JevToolSet(jev).AllTools(),
            cancellationToken);
        Console.WriteLine();
        Console.WriteLine("CLI-native equivalent: use the checked-in jev-decisions skill and jev-engineer custom agent under .github/.");
    }
}
