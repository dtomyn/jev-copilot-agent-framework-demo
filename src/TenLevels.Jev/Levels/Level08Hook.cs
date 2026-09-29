using Jev.Core;

namespace TenLevels.Jev.Levels;

public sealed class Level08Hook : ILevelDemo
{
    public int Number => 8;
    public string Name => "Native Copilot preToolUse hook";
    public string Summary => "The same bounded gate is wired into .github/hooks/jev-policy.json for Copilot CLI.";
    public bool RequiresCopilot => false;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        var gate = new CopilotToolGate(jev);
        GateResult result = await gate.EvaluateAsync(
            "bash",
            "{\"command\":\"dotnet add src/App/App.csproj package Some.Package\"}",
            cancellationToken);
        Console.WriteLine($"preToolUse decision: {result.Decision}");
        Console.WriteLine($"reason: {result.Reason}");
        Console.WriteLine("See .github/hooks/jev-policy.json and src/Jev.CopilotHook/Program.cs for the actual Copilot CLI hook.");
    }
}
