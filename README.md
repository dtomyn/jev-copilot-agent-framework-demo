# Ten Levels of Jev for .NET + GitHub Copilot

A .NET-first learning repo that starts with a single Jev probability and ends with Jev embedded in a GitHub Copilot coding-agent workflow.

The structure is deliberately inspired by [`disler/ten-levels-of-jev`](https://github.com/disler/ten-levels-of-jev/tree/main/apps/ten-levels): each level adds one idea, the early levels run without a coding agent, and the later levels let the coding agent decide when Jev is useful. The Microsoft Agent Framework direction is informed by [`rwjdk/agent-framework-samples`](https://github.com/rwjdk/agent-framework-samples/tree/main/src/JevClassification), but this repo is an original implementation rather than a source port.

This version uses **GitHub Copilot CLI** throughout. The whole demo is C#/.NET 10 except for tiny cross-platform hook wrapper scripts.

## What you get

```text
.
├── .github/
│   ├── agents/jev-engineer.agent.md     # Copilot custom agent
│   ├── hooks/jev-policy.json            # native Copilot CLI preToolUse hook
│   ├── skills/jev-decisions/SKILL.md    # Copilot agent skill
│   ├── workflows/ci.yml
│   └── copilot-instructions.md
├── docs/
│   ├── ARCHITECTURE.md
│   ├── LEVELS.md
│   ├── SECURITY.md
│   ├── SOURCES.md
│   └── tutorial.html                    # step-by-step walkthrough + presenter notes
├── samples/hooks/                       # hook payloads + their expected decision
├── scripts/
│   ├── bootstrap.sh / bootstrap.ps1
│   ├── jev-hook.sh / jev-hook.ps1
│   └── test-hook.sh / test-hook.ps1
├── src/
│   ├── Jev.Core/                        # typed Jev client + mock + risk rules + policy gate
│   ├── Jev.CopilotHook/                 # stdin/stdout Copilot hook adapter
│   └── TenLevels.Jev/                   # levels 01-10
├── tests/Jev.Core.SelfTests/            # dependency-free smoke tests
├── global.json                          # pins the .NET 10 SDK
├── NuGet.config                         # single pinned feed (central package management)
└── JevCopilotDemo.sln
```

## The ten levels

| Level | Name | What changes |
|---:|---|---|
| 01 | The smart if-statement | One Jev Noul probability drives an ordinary C# branch. |
| 02 | Typed routing | A bounded Choice replaces fuzzy routing logic. |
| 03 | Ordered risk | A Score rates state against an explicit ordered rubric. |
| 04 | Parallel typed judgments | Noul, Choice, and Score share one state/request. |
| 05 | Code owns the policy | Deterministic hard rules and code-owned confidence thresholds wrap Jev. |
| 06 | Jev becomes a Copilot tool | Microsoft Agent Framework exposes one Jev function to a GitHub Copilot-backed `AIAgent`. |
| 07 | Copilot gets the Jev toolbelt | Copilot receives Noul, Choice, and Score function tools. |
| 08 | Native Copilot `preToolUse` hook | The same gate is wired into `.github/hooks/jev-policy.json`. |
| 09 | Confidence-aware escalation | Safe reads can pass, hard hazards deny, ambiguity becomes Copilot's human `ask` flow. |
| 10 | The agent reaches for Jev itself | Copilot decides whether a bounded Jev primitive is useful; the repo skill teaches the same behavior in native CLI sessions. |

See [`docs/LEVELS.md`](docs/LEVELS.md) for the rationale.
For a guided, command-by-command walkthrough with presenter notes, open [`docs/tutorial.html`](docs/tutorial.html) in a browser.

## Prerequisites

For Levels 1-5, 8, and 9 you only need the **.NET 10 SDK** (pinned by `global.json`). They work with the deterministic local Jev mock and need no API key.

For live Jev, set a TypeSafe API key in `TYPESAFE_API_KEY`. The client sends `POST https://api.typesafe.ai/v1/systemone` and defaults to `JEV_MODEL=jev-latest`.

For Levels 6, 7, and 10, install and authenticate **GitHub Copilot CLI**. The repo pins `Microsoft.Agents.AI.GitHub.Copilot` 1.22.0 and uses `CopilotClient.AsAIAgent(...)` from Microsoft Agent Framework.

Note that `GitHub.Copilot.SDK` (pulled in transitively) downloads the Copilot CLI from `registry.npmjs.org` **at build time**. On a restricted network that download is the only thing that fails; see [Restricted networks](#restricted-networks).

## Bootstrap

### macOS / Linux

```bash
./scripts/bootstrap.sh
```

### Windows PowerShell 7+

```powershell
./scripts/bootstrap.ps1
```

Bootstrap builds `Jev.Core`, `Jev.CopilotHook`, and the self-tests first, runs the self-tests, replays every sample hook payload, and only then builds the Copilot-backed `TenLevels.Jev`. Building `Jev.CopilotHook` matters because the repository hook executes the compiled DLL rather than doing a restore/build inside the hook timeout.

### Restricted networks

If the `TenLevels.Jev` build fails on `registry.npmjs.org`, everything except Levels 6, 7, and 10 still works. To build those levels anyway, pick one:

```bash
dotnet build src/TenLevels.Jev -c Release -p:CopilotSkipCliDownload=true   # then set COPILOT_CLI_PATH
dotnet build src/TenLevels.Jev -c Release -p:CopilotNpmRegistryUrl=https://your-mirror.example.com
dotnet build src/TenLevels.Jev -c Release -p:CopilotCliBinaryPath=/absolute/path/to/copilot
```

## Run the progression

List levels:

```bash
dotnet run --project src/TenLevels.Jev -- --list
```

Run one level with deterministic mock Jev:

```bash
dotnet run --project src/TenLevels.Jev -- --level 5 --jev mock
```

Run all non-Copilot levels:

```bash
dotnet run --project src/TenLevels.Jev -- --all --jev mock
```

Run with the real Jev API:

```bash
export TYPESAFE_API_KEY="..."
dotnet run --project src/TenLevels.Jev -- --level 4 --jev live
```

PowerShell equivalent:

```powershell
$env:TYPESAFE_API_KEY = "..."
dotnet run --project src/TenLevels.Jev -- --level 4 --jev live
```

## Run the Microsoft Agent Framework + Copilot levels

Once GitHub Copilot CLI is installed and authenticated:

```bash
dotnet run --project src/TenLevels.Jev -- --level 6 --jev mock
dotnet run --project src/TenLevels.Jev -- --level 7 --jev mock
dotnet run --project src/TenLevels.Jev -- --level 10 --jev mock
```

Use `--jev live` instead after setting `TYPESAFE_API_KEY`.

These examples deliberately give the Agent Framework agent only Jev function tools. They do **not** grant its built-in shell/file/URL permissions. That keeps the lesson focused and prevents the MAF sample from becoming a second, hidden execution-policy surface.

One interaction is worth knowing before you adapt this: **the repository hook also sees the tool calls the Agent Framework agent makes**, including the `jev_noul` / `jev_choice` / `jev_score` functions the host registered itself. The gate therefore treats those three as non-mutating, after the hard-deny and credential checks have already run. Without that, the repo's own hook denies the agent's own Jev calls and levels 6, 7, and 10 fail with a fail-closed hook error.

## Run the native GitHub Copilot CLI hook

The checked-in `.github/hooks/jev-policy.json` registers a `preToolUse` command hook. GitHub Copilot sends the proposed tool name and arguments to the .NET hook adapter before execution.

On Windows the hook entry invokes the wrapper as `-NoProfile -ExecutionPolicy Bypass -File`. A default Windows execution policy refuses to load an unsigned `.ps1`, the hook then exits non-zero, and because `preToolUse` is fail-closed that silently denies **every** tool call in the session.

Choose a mode **before starting/restarting Copilot CLI**, because repository hook configuration is loaded when the CLI starts.

### Safe demo: local mock

macOS/Linux:

```bash
export JEV_MODE=mock
copilot
```

PowerShell:

```powershell
$env:JEV_MODE = "mock"
copilot
```

Try prompts that cause a read, a repository edit/package change, and an obviously destructive command. The demo policy is designed to show all three outcomes:

```text
allow  -> low-risk/read-only fast path or high-confidence low risk
ask    -> ambiguous/consequential mutation; Copilot asks the person
deny   -> deterministic hard hazard or sufficiently strong Jev unsafe classification
```

### Live Jev hook

macOS/Linux:

```bash
export JEV_MODE=live
export TYPESAFE_API_KEY="..."
copilot
```

PowerShell:

```powershell
$env:JEV_MODE = "live"
$env:TYPESAFE_API_KEY = "..."
copilot
```

### No Jev network call

`JEV_MODE=off` is the hook default. In that mode, deterministic hard-deny rules still apply, known read-only tools still pass, and other mutations become `ask`.

`JEV_MODE=auto` uses live Jev only when `TYPESAFE_API_KEY` is present; otherwise it behaves like `off`.

## Test the hook without starting Copilot

macOS/Linux:

```bash
./scripts/test-hook.sh
```

PowerShell:

```powershell
./scripts/test-hook.ps1
```

Each sample carries the decision it must produce, so the script is an assertion, not just a printout:

```text
PASS  curl-pipe-shell      -> deny
PASS  dotnet-format        -> ask
PASS  edit                 -> ask
PASS  force-push           -> deny
PASS  force-with-lease     -> ask
PASS  jev-tool-with-secret -> ask
PASS  jev-tool             -> allow
PASS  read                 -> allow
PASS  secret-read          -> ask
```

Or pipe an individual sample payload:

```bash
JEV_MODE=mock ./scripts/jev-hook.sh < samples/hooks/edit.json
```

The hook writes exactly one final JSON decision object to stdout, for example:

```json
{"permissionDecision":"ask","permissionDecisionReason":"Jev did not clear the confidence threshold; require human approval."}
```

## Use the Copilot skill and custom agent

The repository includes a native Copilot skill at `.github/skills/jev-decisions/SKILL.md`. Copilot can select it when a task fits its description, or you can ask for it explicitly in a prompt with `/jev-decisions`.

The custom agent is `.github/agents/jev-engineer.agent.md`. In Copilot CLI you can select it with `/agent`, or launch it directly:

```bash
copilot --agent=jev-engineer --prompt "Review this change and use Jev only if a bounded semantic decision would help."
```

The agent profile does not pre-approve shell access. The repository hook remains in the normal Copilot tool lifecycle.

## Why the policy is split this way

Jev is useful here for **semantic uncertainty**: "is this action security-sensitive?", "which bounded route best fits?", or "where on this explicit risk rubric does this land?" It should not replace facts that C# code, Git, a parser, a compiler, or tests can determine exactly.

So `CopilotToolGate` applies layers in this order:

1. deterministic hard-deny patterns for explicitly destructive commands;
2. a deterministic read-only fast path;
3. optional Jev `Choice(allow, ask, deny)` classification;
4. explicit code-owned probability/confidence thresholds;
5. human `ask` for everything that does not clear the threshold.

The important design principle is **Jev advises; code authorizes**.

## Hook failure semantics you should know

GitHub's current Copilot hook contract matters for security design:

- A `preToolUse` command hook that crashes or returns a non-zero exit is fail-closed and denies the tool call.
- A hook **timeout is fail-open to Copilot's normal permission flow**, not an automatic deny.
- Under Copilot cloud agent there is no person to answer `ask`, so `ask` is treated as `deny`.

Because a timeout is the one failure mode that fails open, the hook enforces its **own** deadline (`JEV_HOOK_DEADLINE_MS`, default 4000 ms) below the hook's `timeoutSec` of 8, so a slow decision returns an explicit `ask` instead of letting Copilot fall back to its default flow. The live Jev HTTP call is additionally capped at two seconds, and ordinary Jev/client exceptions become an explicit `ask` with exit code zero. See [`docs/SECURITY.md`](docs/SECURITY.md) before adapting the pattern to production.

## Jev API shape used here

The TypeSafe API accepts one `state`, a model name, and named typed questions. This repo implements the three primitives used by the current API:

```json
{
  "state": "Copilot wants to update a package and regenerate snapshots.",
  "model": "jev-latest",
  "questions": {
    "side_effect": {
      "type": "noul",
      "instructions": "Does this have persistent side effects?"
    },
    "route": {
      "type": "choice",
      "instructions": "How should this be handled?",
      "criteria": {
        "allow": "Low-risk and reversible",
        "ask": "Consequential or ambiguous",
        "deny": "Clearly prohibited"
      }
    },
    "risk": {
      "type": "score",
      "instructions": "Rate operational risk",
      "criteria": ["read-only", "small mutation", "broad mutation", "destructive"]
    }
  }
}
```

The client is intentionally tiny and lives in `src/Jev.Core/JevHttpClient.cs`, so the wire contract is easy to inspect.

## Suggested demo script

For a 10-15 minute walkthrough:

1. Run Levels 1-4 in mock mode to introduce Noul/Choice/Score and parallel questions.
2. Run Level 5 to show that policy stays in C#.
3. Run Level 7 with an authenticated Copilot runtime to show Jev as MAF function tools.
4. Run `./scripts/test-hook.sh` to show the exact native `preToolUse` JSON contract.
5. Start `JEV_MODE=mock copilot`, make Copilot inspect a file, edit a file, and propose a force push; point out `allow`, `ask`, and `deny`.
6. End on Level 10 plus `/jev-decisions`: the agent learns when a fast typed decision is useful, without turning Jev into an unconstrained planner.

## Production hardening ideas

This repo intentionally keeps the sample readable. The deterministic rules in `src/Jev.Core/RiskHeuristics.cs` are anchored regular expressions rather than substring tests, and each one is pinned by a self-test in both directions, but a regex still is not a shell parser: `rm $VAR` and base64-encoded payloads are outside its reach. Before using the pattern as organizational policy, consider signed/versioned policy distribution, real shell/command-line parsing, dedicated secret redaction, audit/event logging, latency and error telemetry, organization-specific allow/deny rules, sandboxing, policy tests derived from real tool traces, and a clear decision on whether a remote Jev call is permitted for the data being evaluated.

## Sources and compatibility

See [`docs/SOURCES.md`](docs/SOURCES.md). The implementation was checked against the GitHub Copilot CLI hook documentation, the GitHub Copilot skill/custom-agent documentation, the Microsoft Agent Framework GitHub Copilot provider docs, the current TypeSafe OpenAPI document, and the current NuGet package metadata while this demo was assembled in September 2026.

MIT licensed. This is an independent demo and is not an official TypeSafe, GitHub, or Microsoft project.
