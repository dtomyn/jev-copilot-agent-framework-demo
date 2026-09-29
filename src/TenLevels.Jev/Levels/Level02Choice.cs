using Jev.Core;

namespace TenLevels.Jev.Levels;

public sealed class Level02Choice : ILevelDemo
{
    public int Number => 2;
    public string Name => "Typed routing";
    public string Summary => "Replace a pile of fuzzy conditionals with a bounded Choice.";
    public bool RequiresCopilot => false;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        const string state = "Copilot proposes to install a new global CLI package and update PATH.";
        var response = await jev.DecideAsync(state, new Dictionary<string, JevQuestion>
        {
            ["route"] = new ChoiceQuestion(
                "Choose the safest handling route for this proposed action.",
                new Dictionary<string, string?>
                {
                    ["allow"] = "Low-risk and repository-local.",
                    ["ask"] = "Consequential or ambiguous; require a person to approve.",
                    ["deny"] = "Clearly destructive, secret-exposing, system-wide, or irreversible.",
                }),
        }, cancellationToken);

        LevelOutput.Answer("route", response.Answers["route"]);
    }
}
