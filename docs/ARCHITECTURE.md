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
             +-------------+                    +-----------------+
             | JevToolSet  |                    | Jev.CopilotHook |
             +------+------+                    +--------+--------+
                    |                                    |
                    +-----------------+------------------+
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
                          +-----------+----------+
                          v                      v
                  TypeSafe Jev API       deterministic mock
```

## Two Copilot integration tracks

### Microsoft Agent Framework track

Levels 6, 7, and 10 use `Microsoft.Agents.AI.GitHub.Copilot`. The Copilot SDK remains the backend and Agent Framework exposes it as an `AIAgent`. Jev is registered as ordinary `AIFunction` tools (`CopilotClient.AsAIAgent(tools:)` takes `IList<AIFunctionDeclaration>`). The examples intentionally do not grant the programmatic agent shell/file permissions, so they demonstrate decision composition without silently creating a second execution policy surface.

The two tracks are not independent. The Copilot runtime the Agent Framework spawns runs in the repository working directory, so it loads `.github/hooks/jev-policy.json` and applies the `preToolUse` hook to the agent's tool calls as well, including the Jev functions the host registered. `CopilotToolGate` therefore recognises `jev_noul`, `jev_choice`, and `jev_score` as non-mutating, after the hard-deny and credential checks. Removing that makes the repository's own hook deny the agent's own Jev calls.

### Native Copilot CLI hook track

Levels 8 and 9 use `.github/hooks/jev-policy.json`. Copilot sends each `preToolUse` payload to a small .NET console program. That adapter applies deterministic rules first, optionally calls Jev, and returns one of `allow`, `ask`, or `deny`.

The native hook is the right place to demonstrate enforcement because it participates directly in Copilot CLI's tool lifecycle.

## Policy layering

1. **Hard deterministic deny** for explicit destructive command patterns.
   These are anchored regular expressions in `src/Jev.Core/RiskHeuristics.cs`, not substring tests.
   Substring tests fail in both directions: `curl https://x/install.sh | sh` never contains the
   literal `curl | sh`, and `dotnet format` is not a disk format.
   Each pattern is pinned by a self-test that asserts both a matching and a non-matching command.
2. **Sensitive-material stop** for anything that looks like credential material, which escalates to
   a person and is never forwarded to Jev.
3. **Read-only fast path** for known non-mutating Copilot tools.
4. **Jev semantic classification** when enabled (`mock` or `live`).
5. **Code-owned thresholds** translate Jev probabilities/confidence into `allow`, `ask`, or `deny`.
6. **Human approval** is the default for ambiguous mutations.

This is deliberately not `model says safe -> execute`.

## Hook failure behavior

The wrapper returns `ask` if the .NET hook binary has not been built. The .NET adapter also degrades Jev/network exceptions to `ask` with exit code zero. This avoids accidental approval due to a local exception.

The wrapper itself must also be able to start. On Windows the hook entry runs the PowerShell wrapper with `-NoProfile -ExecutionPolicy Bypass -File`, because the default execution policy refuses to load an unsigned `.ps1`; that refusal is a non-zero exit, and `preToolUse` is fail-closed, so it would deny every tool call in the session rather than just failing the hook.

GitHub Copilot itself treats command-hook crashes/non-zero exits for `preToolUse` as deny, but hook **timeouts** fall back to Copilot's normal permission flow.

Because the timeout is the one path that fails open, the adapter does not rely on the outer timeout at all. It starts a `CancellationTokenSource` with its own deadline (`JEV_HOOK_DEADLINE_MS`, default 4000 ms) that covers reading stdin and the whole gate evaluation, and converts expiry into an explicit `ask`. The layering is:

```text
JEV_HOOK_DEADLINE_MS (4s)  <  timeoutSec in jev-policy.json (8s)
  live Jev HTTP timeout (2s)
```

Organizations needing strict fail-closed timeout semantics should still enforce that separately at an enterprise policy layer rather than assuming a hook timeout blocks the tool.
