using GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace TenLevels.Jev.Copilot;

/// <summary>
/// Hosts GitHub Copilot as a Microsoft Agent Framework <see cref="AIAgent"/> and lets it call the
/// Jev tools. The agent is given no shell/file tools of its own: these levels are about composing
/// typed judgments, not about creating a second execution-policy surface next to the hook.
/// </summary>
public static class CopilotAgentRunner
{
    public static async Task RunAsync(
        string instructions,
        string prompt,
        IList<AIFunctionDeclaration> tools,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Starting the Microsoft Agent Framework GitHub Copilot agent...");

        var options = new CopilotClientOptions();

        // Point at a specific Copilot runtime when one is configured; otherwise the SDK uses the
        // runtime bundled by the GitHub.Copilot.SDK build targets.
        string? cliPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(cliPath))
        {
            options.Connection = RuntimeConnection.ForStdio(cliPath);
        }

        await using var client = new CopilotClient(options);
        await client.StartAsync(cancellationToken);

        AIAgent agent = client.AsAIAgent(tools: tools, instructions: instructions);

        AgentResponse response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
        Console.WriteLine(response);
    }
}
