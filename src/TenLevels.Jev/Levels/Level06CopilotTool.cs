using Jev.Core;
using TenLevels.Jev.Copilot;

namespace TenLevels.Jev.Levels;

public sealed class Level06CopilotTool : ILevelDemo
{
    public int Number => 6;
    public string Name => "The decision engine becomes a Copilot tool";
    public string Summary => "Microsoft Agent Framework runs GitHub Copilot; Copilot can call one narrow decision function tool.";
    public bool RequiresCopilot => true;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        var toolSet = new DecisionToolSet(jev);
        string provider = LevelOutput.ProviderName(jev);
        await CopilotAgentRunner.RunAsync(
            $"You are a coding assistant. For the requested semantic yes/no judgment, call {toolSet.NoulToolName} instead of inventing a probability. Explain the code-owned threshold after the call.",
            $"A PR changes authentication middleware and token refresh code. Ask {provider} whether security-focused review is warranted. Treat probability >= 0.70 as requiring security review.",
            toolSet.NoulOnly(),
            cancellationToken);
    }
}
