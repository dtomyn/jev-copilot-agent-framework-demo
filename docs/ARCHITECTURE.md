# Architecture

## Design goal

Keep the "ten levels" teaching shape while making the implementation .NET-first and treating GitHub Copilot CLI as the coding-agent runtime.

```text
                         +-------------------------+
                         | GitHub Copilot CLI      |
                         | or MAF Copilot provider |
                         +------------+------------+
                                      |
                    +-----------------+------------------+
                    |                                    |
          function tools (6/7/10)              preToolUse hook (8/9)
                    |                                    |
                    v                                    v
          +------------------+                  +-----------------+
          | DecisionToolSet  |                  | Jev.CopilotHook |
          +--------+---------+                  +--------+--------+
                   |                                     |
                   +-----------------+-------------------+
                                     v
                             +---------------+
                             |   Jev.Core    |
                             |---------------|
                             | IJevClient    |
                             | HTTP / Mock   |
                             | typed DTOs    |
                             | policy gate   |
                             +-------+-------+
                                     |
                             live    |    offline
                  +------------------+----------+
                  v                             v
      POST /v1/systemone                 deterministic mock
      +-----------------+
      | TypeSafe Jev    |  hosted
      | laya-serve      |  open source, local container
      +-----------------+
```

## One wire contract, two providers

`laya-serve` implements the same `POST /v1/systemone` request and response shape as TypeSafe Jev, so `SystemOneHttpClient` and the DTOs in `Models.cs` serve both. Everything that differs is data, held in `SystemOneEndpoint`: base address, whether a bearer token is sent at all, and whether the `model` field is sent or left to Laya's router.

Exactly one semantic difference survives into the answer: `confidence` is `(n*p_max - 1)/(n - 1)` on Jev and normalized entropy on Laya. Rather than branch on the provider in policy code, the client normalizes at the boundary and exposes `AnswerConfidence` (`max(p)`, read from Laya's `answer_confidence` and recomputed from the distribution for Jev). Policy reads only that, so `CopilotToolGate` contains no provider-specific code at all. See [LAYA.md](LAYA.md).

## Two Copilot integration tracks

### Microsoft Agent Framework track

Levels 6, 7, and 10 use `Microsoft.Agents.AI.GitHub.Copilot`. The Copilot SDK remains the backend and Agent Framework exposes it as an `AIAgent`. Jev is registered as ordinary `AIFunction` tools (`CopilotClient.AsAIAgent(tools:)` takes `IList<AIFunctionDeclaration>`). The examples intentionally do not grant the programmatic agent shell/file permissions, so they demonstrate decision composition without silently creating a second execution policy surface.

The two tracks are not independent. The Copilot runtime the Agent Framework spawns runs in the repository working directory, so it loads `.github/hooks/jev-policy.json` and applies the `preToolUse` hook to the agent's tool calls as well, including the decision functions the host registered. `CopilotToolGate` therefore recognises `jev_noul`, `jev_choice`, and `jev_score` as non-mutating, after the hard-deny and credential checks - plus the `laya_*` spellings, because `DecisionToolSet` names its tools after the selected provider and the hook cannot know which provider another process chose. Removing that makes the repository's own hook deny the agent's own decision calls.

### Native Copilot CLI hook track

Levels 8 and 9 use `.github/hooks/jev-policy.json`. Copilot sends each `preToolUse` payload to a small .NET console program. That adapter applies deterministic rules first, optionally calls the selected provider, and returns one of `allow`, `ask`, or `deny`.

The native hook is the right place to demonstrate enforcement because it participates directly in Copilot CLI's tool lifecycle.

## Presentation front end

`src/Jev.WebUi` is an ASP.NET Core minimal API plus a dependency-free page. It is a third caller of the same core, not a third policy:

- Levels 1 to 4 post to `/api/decide`, which builds the level's typed questions from the form and calls `IJevClient` directly.
- Levels 5 and 9 post to `/api/gate`, which calls `CopilotToolGate` and reports `GateResult.Stage` so the page can say which layer decided and whether a provider was contacted at all.
- Level 8 posts to `/api/hook/run`, which starts `Jev.CopilotHook` as a child process with the payload on stdin. Nothing is simulated: the panel shows the raw stdout, the exit code, and whether the "exit 0 and exactly one JSON object" contract held. Calling the gate in-process and formatting the result to look like hook output would hide precisely the failure modes levels 8 and 9 are about.
- Levels 6, 7 and 10 post to `/api/tool` for a single function-tool call, and stream `/api/cli/stream` for a full agent run, which spawns `dotnet run --project src/TenLevels.Jev`.

The written tutorial is not a separate artefact. `TutorialNotes` parses `docs/tutorial.html`, collects every `<div class="notes" data-level="...">` block, and indexes it by level, so the UI's presenter notes and the tutorial's are the same paragraphs. The UI also serves the tutorial at `/tutorial`, which makes the links in both directions same-origin: a note in the UI links to the section it came from, and each level section in the tutorial carries a deep link into the corresponding level panel. The `data-level` attribute is the only coupling, and a block without one simply does not appear in the UI.

Two constraints shape the project. It must not reference `TenLevels.Jev`, because that project downloads the Copilot CLI from npm at build time and the UI is what a presenter starts first; the Copilot-backed levels are therefore reached by spawning the CLI rather than linking to it. And the level catalogue is parsed out of the `ILevelDemo` implementations at runtime, including their full source text, so the code on screen is the code on disk and a renamed level renames itself.

## Policy layering

1. **Hard deterministic deny** for explicit destructive command patterns.
   These are anchored regular expressions in `src/Jev.Core/RiskHeuristics.cs`, not substring tests.
   Substring tests fail in both directions: `curl https://x/install.sh | sh` never contains the
   literal `curl | sh`, and `dotnet format` is not a disk format.
   Each pattern is pinned by a self-test that asserts both a matching and a non-matching command.
2. **Sensitive-material stop** for anything that looks like credential material, which escalates to
   a person and is never forwarded to the model.
3. **Read-only fast path** for known non-mutating Copilot tools.
4. **Semantic classification** by the selected provider when enabled (`mock` or `live`).
5. **Code-owned thresholds** translate the returned probabilities into `allow`, `ask`, or `deny`.
   They read `max(p)`, never the provider's own `confidence` field, which the two engines define
   differently.
6. **Human approval** is the default for ambiguous mutations.

This is deliberately not `model says safe -> execute`.

## Hook failure behavior

The wrapper returns `ask` if the .NET hook binary has not been built. The .NET adapter also degrades provider/network exceptions to `ask` with exit code zero. This avoids accidental approval due to a local exception.

The wrapper itself must also be able to start. On Windows the hook entry runs the PowerShell wrapper with `-NoProfile -ExecutionPolicy Bypass -File`, because the default execution policy refuses to load an unsigned `.ps1`; that refusal is a non-zero exit, and `preToolUse` is fail-closed, so it would deny every tool call in the session rather than just failing the hook.

GitHub Copilot itself treats command-hook crashes/non-zero exits for `preToolUse` as deny, but hook **timeouts** fall back to Copilot's normal permission flow.

Because the timeout is the one path that fails open, the adapter does not rely on the outer timeout at all. It starts a `CancellationTokenSource` with its own deadline (`DECISION_HOOK_DEADLINE_MS`, default 4000 ms) that covers reading stdin and the whole gate evaluation, and converts expiry into an explicit `ask`. The layering is:

```text
DECISION_HOOK_DEADLINE_MS (4s)  <  timeoutSec in jev-policy.json (8s)
  live HTTP timeout (2s)
```

The 2-second cap matters most against a local Laya container: once warm a decision takes tens of milliseconds, but the first request after a cold start loads a checkpoint and takes far longer than any hook budget. That becomes an explicit `ask`, never a silent allow. `scripts/laya-up.*` waits for `/health` before returning for the same reason.

Organizations needing strict fail-closed timeout semantics should still enforce that separately at an enterprise policy layer rather than assuming a hook timeout blocks the tool.
