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
- Laya, the open-source System 1 decision engine
  https://github.com/NandhaKishorM/laya
- Laya HTTP API reference (`laya-serve`, the `/v1/systemone` compatibility surface)
  https://nandhakishorm.github.io/laya/

## Verified against this repository

The hook input fields (`sessionId`, `timestamp`, `cwd`, `toolName`, `toolArgs`), the output object
(`permissionDecision` of `allow`/`ask`/`deny` plus `permissionDecisionReason`), the exit-code
semantics, and the fail-open timeout behaviour were taken from the hooks reference above. The
request/response DTOs in `src/Jev.Core/Models.cs` mirror the `SystemOneRequest`/`SystemOneResponse`
schemas in the TypeSafe OpenAPI document, and `tests/Jev.Core.SelfTests` asserts the serialized
wire shape against a stub `HttpMessageHandler`. `CopilotClient.AsAIAgent(tools:)` takes
`IList<AIFunctionDeclaration>` and returns an `AgentResponse` in
`Microsoft.Agents.AI.GitHub.Copilot` 1.22.0.

The Laya provider was checked against Laya's HTTP API documentation and its `laya/serve.py` and
`laya/agent.py` sources: the `/v1/systemone` route and its Jev compatibility intent, the optional
`model` field and its router fallback, the optional `LAYA_API_KEY` bearer check, the
`answer_confidence` / `confidence` distinction and the two formulas behind it, the `routing` block,
and the documented `400` / `401` / `413` / `422` / `503` responses. The self-tests encode that shape
against the same stub handler, so a future upstream change surfaces as a test failure rather than a
runtime surprise.

Documentation and package APIs evolve. Re-check the current docs before promoting the sample into organizational policy.

## Decider references

The Decider integration was checked against the public project and package surfaces current in September 2026:

- Decider repository and server examples
  https://github.com/Mapika/decider
- `Mapika/decider-4b` model card
  https://huggingface.co/Mapika/decider-4b
- `decider-ai` package
  https://pypi.org/project/decider-ai/
- Docker Desktop GPU support on Windows/WSL 2
  https://docs.docker.com/desktop/features/gpu/

The integration relies on Decider's Jev-compatible `POST /v1/systemone` route and `/health` endpoint. The application reads Decider's `x_p_max` as `AnswerConfidence` while retaining the provider's own `confidence` for display. The local Docker image pins `decider-ai` 1.6.0 by default and selects `Mapika/decider-4b` through the server-side `DECIDER_MODEL` environment variable. Upstream currently supports GGUF through the Python engine, but its HTTP/schema path still uses the Torch engine; the Docker HTTP path in this repository therefore uses the Torch model rather than advertising GGUF as a drop-in server replacement.
