using Jev.Core;
using TenLevels.Jev.Copilot;

namespace TenLevels.Jev.Levels;

public sealed class Level06CopilotTool : ILevelDemo
{
    public int Number => 6;
    public string Name => "Jev becomes a Copilot tool";
    public string Summary => "Microsoft Agent Framework runs GitHub Copilot; Copilot can call one narrow Jev function tool.";
    public bool RequiresCopilot => true;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        var tools = new JevToolSet(jev).NoulOnly();
        await CopilotAgentRunner.RunAsync(
            "You are a coding assistant. For the requested semantic yes/no judgment, call jev_noul instead of inventing a probability. Explain the code-owned threshold after the call.",
            "A PR changes authentication middleware and token refresh code. Ask Jev whether security-focused review is warranted. Treat probability >= 0.70 as requiring security review.",
            tools,
            cancellationToken);
    }
}
