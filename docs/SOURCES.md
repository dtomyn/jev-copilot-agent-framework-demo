# Design references

This repository is an original demo informed by these public projects and documentation. It does not copy their source code.

## Inspiration repositories

- `disler/ten-levels-of-jev`, especially `apps/ten-levels`: the incremental ten-level teaching structure and the idea of evolving from direct Jev calls into coding-agent integration.
- `rwjdk/agent-framework-samples`, `src/JevClassification`: the .NET + Microsoft Agent Framework direction requested for this demo.

## Product/API documentation checked while building the demo

- GitHub Docs: Using hooks with GitHub Copilot CLI
  https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-hooks
- GitHub Docs: GitHub Copilot hooks reference
  https://docs.github.com/en/copilot/reference/hooks-reference
- GitHub Docs: Adding agent skills for GitHub Copilot CLI
  https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-skills
- GitHub Docs: Invoking custom agents
  https://docs.github.com/en/copilot/how-tos/copilot-cli/use-copilot-cli/invoke-custom-agents
- Microsoft Learn: Agent Framework - GitHub Copilot provider
  https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/agent-services/github-copilot
- TypeSafe Jev OpenAPI
  https://api.typesafe.ai/openapi.json

## Verified against this repository

The hook input fields (`sessionId`, `timestamp`, `cwd`, `toolName`, `toolArgs`), the output object
(`permissionDecision` of `allow`/`ask`/`deny` plus `permissionDecisionReason`), the exit-code
semantics, and the fail-open timeout behaviour were taken from the hooks reference above. The Jev
request/response DTOs in `src/Jev.Core/Models.cs` mirror the `SystemOneRequest`/`SystemOneResponse`
schemas in the TypeSafe OpenAPI document, and `tests/Jev.Core.SelfTests` asserts the serialized
wire shape against a stub `HttpMessageHandler`. `CopilotClient.AsAIAgent(tools:)` takes
`IList<AIFunctionDeclaration>` and returns an `AgentResponse` in
`Microsoft.Agents.AI.GitHub.Copilot` 1.22.0.

Documentation and package APIs evolve. Re-check the current docs before promoting the sample into organizational policy.
