using Jev.Core;
using TenLevels.Jev.Copilot;

namespace TenLevels.Jev.Levels;

public sealed class Level07CopilotToolbelt : ILevelDemo
{
    public int Number => 7;
    public string Name => "Copilot gets the decision toolbelt";
    public string Summary => "Expose Noul, Choice, and Score tools and let the agent compose narrow typed judgments.";
    public bool RequiresCopilot => true;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        string provider = LevelOutput.ProviderName(jev);
        await CopilotAgentRunner.RunAsync(
            $"You are a coding triage agent. Use {provider} only for bounded semantic decisions; use ordinary reasoning for arithmetic and deterministic facts. Keep policy decisions in your own explicit rules.",
            $"Triage this task: 'Upgrade a logging package, regenerate snapshots, and commit the result.' Use {provider} to choose allow/ask/deny and to score operational risk on four descriptive levels. Summarize both outputs without changing files.",
            new DecisionToolSet(jev).AllTools(),
            cancellationToken);
    }
}
