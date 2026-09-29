using Jev.Core;

namespace TenLevels.Jev.Levels;

public sealed class Level05Policy : ILevelDemo
{
    public int Number => 5;
    public string Name => "Code owns the policy";
    public string Summary => "The model advises; deterministic rules and confidence thresholds decide allow/ask/deny.";
    public bool RequiresCopilot => false;

    public async Task RunAsync(IJevClient jev, CancellationToken cancellationToken = default)
    {
        LevelOutput.Header(this);
        var gate = new CopilotToolGate(jev);
        (string Tool, string Args)[] samples =
        [
            ("view", "{\"path\":\"src/Program.cs\"}"),
            ("bash", "{\"command\":\"dotnet add package Example.Package\"}"),
            ("bash", "{\"command\":\"git push --force origin main\"}"),
        ];

        foreach ((string tool, string args) in samples)
        {
            GateResult result = await gate.EvaluateAsync(tool, args, cancellationToken);
            Console.WriteLine($"{tool,-8} -> {result.Decision,-5} | {result.Reason}");
        }
    }
}
