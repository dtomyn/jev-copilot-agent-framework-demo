using Jev.Core;

namespace TenLevels.Jev.Levels;

public sealed class Level04Parallel : ILevelDemo
{
    public int Number => 4;
    public string Name => "Parallel typed judgments";
    public string Summary => "Ask Noul, Choice, and Score questions together against one state.";
    public bool RequiresCopilot => false;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        const string state = "Copilot wants to run dotnet test, then git commit the generated snapshot updates.";
        var response = await jev.DecideAsync(state, new Dictionary<string, JevQuestion>
        {
            ["has_side_effect"] = new NoulQuestion("Does the proposed sequence have persistent side effects?"),
            ["route"] = new ChoiceQuestion("How should the sequence be handled?", new Dictionary<string, string?>
            {
                ["allow"] = "Only harmless/read-only actions.",
                ["ask"] = "Contains a meaningful mutation that a person should approve.",
                ["deny"] = "Contains a clearly prohibited action.",
            }),
            ["risk"] = new ScoreQuestion("Rate operational risk.",
            [
                "Read-only.",
                "Small repository-local mutation.",
                "Broad or stateful mutation.",
                "Destructive or security-sensitive.",
            ]),
        }, cancellationToken);

        foreach ((string name, JevAnswer answer) in response.Answers)
        {
            LevelOutput.Answer(name, answer);
        }
        Console.WriteLine($"model={response.Model}, input_tokens={response.Usage.InputTokens}");

        // Laya routes each request to one of three checkpoints and says which and why. Jev has no
        // equivalent field, so this line simply does not appear there.
        if (response.Routing is { } routing)
        {
            Console.WriteLine($"routing={routing}");
        }
    }
}
