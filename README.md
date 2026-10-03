# Ten Levels of Typed Decisions for .NET + GitHub Copilot

A .NET-first learning repo that starts with a single System-One probability and ends with that decision engine embedded in a GitHub Copilot coding-agent workflow.

Four engines are supported and are selected with one flag: **[TypeSafe Jev](https://api.typesafe.ai)**, hosted; **[Laya](https://github.com/NandhaKishorM/laya)**, open source and local; **[Decider](https://github.com/Mapika/decider)**, open source and local; and **[Clef](https://developers.cloudflare.com/workers-ai/models/clef-flash/)**, Cloudflare's open-weights model, hosted on Workers AI or local in Docker. Laya, Decider, and Clef all expose the same System-One wire protocol, so one client and one set of DTOs serve all four; see [`docs/LAYA.md`](docs/LAYA.md), [`docs/DECIDER.md`](docs/DECIDER.md), and [`docs/CLEF.md`](docs/CLEF.md).

The structure is deliberately inspired by [`disler/ten-levels-of-jev`](https://github.com/disler/ten-levels-of-jev/tree/main/apps/ten-levels): each level adds one idea, the early levels run without a coding agent, and the later levels let the coding agent decide when Jev is useful. The Microsoft Agent Framework direction is informed by [`rwjdk/agent-framework-samples`](https://github.com/rwjdk/agent-framework-samples/tree/main/src/JevClassification), but this repo is an original implementation rather than a source port.

This version uses **GitHub Copilot CLI** throughout. The whole demo is C#/.NET 10 except for tiny cross-platform hook wrapper scripts.

## Demo video

A three-minute walkthrough of the [interactive demo UI](#the-interactive-demo-ui), recorded against live TypeSafe Jev.
It covers all ten levels, including the real GitHub Copilot agent on Levels 7 and 10 and the real `preToolUse` hook process on Level 8.



https://github.com/user-attachments/assets/dab5e710-14fb-46c3-a598-cc9047cf215f



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
│   ├── LAYA.md                          # running the demo against local open-source Laya
│   ├── DECIDER.md                       # Windows/Docker setup for local Decider
│   ├── CLEF.md                          # Cloudflare Clef, hosted (Workers AI) or local (Docker)
│   ├── LEVELS.md
│   ├── SECURITY.md
│   ├── SOURCES.md
│   └── tutorial.html                    # step-by-step walkthrough + presenter notes
├── samples/hooks/                       # hook payloads + their expected decision
├── scripts/
│   ├── bootstrap.sh / bootstrap.ps1
│   ├── jev-hook.sh / jev-hook.ps1
│   ├── laya-up.sh / laya-up.ps1         # start/stop the local Laya container
│   ├── laya-down.sh / laya-down.ps1
│   ├── decider-up.sh / decider-up.ps1   # build/start local Decider
│   ├── decider-down.sh / decider-down.ps1
│   ├── clef-up.sh / clef-up.ps1         # build/start local Clef
│   ├── clef-down.sh / clef-down.ps1
│   ├── test-hook.sh / test-hook.ps1
│   └── webui.sh / webui.ps1             # build and start the interactive demo UI
├── src/
│   ├── Jev.Core/                        # typed System-One client + mock + risk rules + policy gate
│   ├── Jev.CopilotHook/                 # stdin/stdout Copilot hook adapter
│   ├── Jev.WebUi/                       # browser UI: code on the left, one-click runs on the right
│   └── TenLevels.Jev/                   # levels 01-10
├── tests/Jev.Core.SelfTests/            # dependency-free smoke tests
├── global.json                          # pins the .NET 10 SDK
├── NuGet.config                         # single pinned feed (central package management)
└── JevCopilotDemo.sln
```

## The ten levels

| Level | Name | What changes |
|---:|---|---|
| 01 | The smart if-statement | One Noul probability drives an ordinary C# branch. |
| 02 | Typed routing | A bounded Choice replaces fuzzy routing logic. |
| 03 | Ordered risk | A Score rates state against an explicit ordered rubric. |
| 04 | Parallel typed judgments | Noul, Choice, and Score share one state/request. |
| 05 | Code owns the policy | Deterministic hard rules and code-owned confidence thresholds wrap the model. |
| 06 | The decision engine becomes a Copilot tool | Microsoft Agent Framework exposes one decision function to a GitHub Copilot-backed `AIAgent`. |
| 07 | Copilot gets the decision toolbelt | Copilot receives Noul, Choice, and Score function tools. |
| 08 | Native Copilot `preToolUse` hook | The same gate is wired into `.github/hooks/jev-policy.json`. |
| 09 | Confidence-aware escalation | Safe reads can pass, hard hazards deny, ambiguity becomes Copilot's human `ask` flow. |
| 10 | The agent reaches for the engine itself | Copilot decides whether a bounded typed primitive is useful; the repo skill teaches the same behavior in native CLI sessions. |

See [`docs/LEVELS.md`](docs/LEVELS.md) for the rationale.
For a guided, command-by-command walkthrough with presenter notes, open [`docs/tutorial.html`](docs/tutorial.html) in a browser.

## The interactive demo UI

For presenting, there is a browser UI that shows each level's real source next to a panel that runs it:

```bash
./scripts/webui.sh          # macOS / Linux
./scripts/webui.ps1         # Windows PowerShell 7+
```

It serves on <http://127.0.0.1:5088> and **defaults to Laya in mock mode**, so it needs no API key, no container and no network.

To present against a real local provider instead, add one switch:

```bash
./scripts/webui.sh --laya             # macOS / Linux, Laya
./scripts/webui.sh --decider          # macOS / Linux, Decider CPU
./scripts/webui.sh --clef-gpu         # macOS / Linux, Clef + NVIDIA GPU
./scripts/webui.ps1 -Laya             # Windows PowerShell 7+, Laya
./scripts/webui.ps1 -Decider          # Windows PowerShell 7+, Decider CPU
./scripts/webui.ps1 -DeciderGpu       # Windows PowerShell 7+, Decider + NVIDIA GPU
./scripts/webui.ps1 -ClefGpu          # Windows PowerShell 7+, Clef + NVIDIA GPU
```

The Laya path starts `laya-serve`; the Decider and Clef paths build/start the local images under `docker/decider` and `docker/clef`. Each waits for `/health`, sets the corresponding base URL for the UI, and then starts it with Live enabled for that provider. You can also start several local providers together to switch between them per request.
Local Clef needs about 19 GB for its weights, so `-Clef`/`--clef` without a GPU works but is too slow for the hook.
Hosted Clef needs no switch at all: set `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN` before starting the UI.
A container that is already running is reused, not restarted.
The script refuses to start if a UI is already listening on port 5088, because that instance keeps the environment it was started with and would still show Live disabled.
It names the process holding the port and prints the command to stop it.
Add `-Restart` (or `--restart`) to stop an earlier demo UI and start the new one in one go; it never stops a process that is not `Jev.WebUi`.

- **Provider and mode switch per request**, from the top bar.
  Live mode is offered only for a provider the environment can actually reach: Jev needs `TYPESAFE_API_KEY`, Laya needs `LAYA_BASE_URL`, Decider needs `DECIDER_BASE_URL` (the helper scripts use `http://127.0.0.1:8011`), and Clef needs either `CLEF_BASE_URL` (`http://127.0.0.1:8012`) or both `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN`.
  The Jev key can also be entered in the **Config** dialog, in a masked field. It is applied as `TYPESAFE_API_KEY` for the UI and every hook and level process it starts, exactly as if it had been set in the shell. It is held in memory only, never written to disk, and never sent back to the page.
  Everything else runs against the deterministic mock.
- **Every scenario is editable.**
  The state, the question wording, the choice set, the score rubric and the code-owned threshold all start at the level's checked-in values and can be changed and re-run.
- **The code pane is read from disk**, so what is on screen is what runs, including the gate and the hook adapter for the levels that are about them.
- **Level 8 starts the real hook executable** as a child process, writes a `preToolUse` payload to its stdin and shows the raw stdout, the exit code, and whether the one-JSON-object contract was satisfied. The checked-in payloads under `samples/hooks/` are in the picker, with their expected decision checked against what the hook actually returned.
- **Levels 6, 7 and 10** offer two things: a sandbox that calls one decision function tool with the arguments an agent would pass, needing no Copilot runtime, and a button that shells out to the real CLI and streams its output.

### The tutorial and the UI are one thing

[`docs/tutorial.html`](docs/tutorial.html) is the written walkthrough and the UI is the live one, and they are wired together rather than maintained in parallel.

- The UI reads its **presenter notes straight out of the tutorial** and shows the ones for the level on screen. A note belongs to a level because its block carries `data-level="5 9"` in the tutorial; that attribute is the whole coupling. Write the note once, in the tutorial, and it appears in both places.
- **The notes collapse two ways.** The card header, and the `Notes` button in the top bar, are the same switch: they fold the whole card down to a single bar for a clean runbook, and that choice is remembered. Inside the card each note is its own disclosure, so a level's notes read as a short index of section titles you open one at a time. Levels with one or two notes open the first; levels with more open none, which keeps the Run panel on screen. `Expand all` is there when you want the lot.
- Each note links back to the tutorial section it came from.
- The tutorial is served at `/tutorial` by the UI, so those links and the `Tutorial` button in the top bar stay same-origin and survive being sent to someone.
- Each level section in the tutorial carries a **Run live** link that opens that level in the UI.

The UI project builds offline: it references `Jev.Core` and `Jev.CopilotHook`, never `TenLevels.Jev`, so the npm download that project needs is not in the way of starting the demo. Pass `--include-agent-levels` (or `-IncludeAgentLevels`) to build that project too.

## Prerequisites

For Levels 1-5, 8, and 9 you only need the **.NET 10 SDK** (pinned by `global.json`). They work with the deterministic local mock and need no API key and no container.

For a live decision engine, pick one:

- **Laya (open source, local).** Run `./scripts/laya-up.ps1` (or `.sh`) to start `laya-serve` in Docker, then use `--provider laya --mode live`. No API key. See [`docs/LAYA.md`](docs/LAYA.md).
- **Decider (open source, local).** On Windows 11, run `.\scripts\decider-up.ps1` for CPU or `.\scripts\decider-up.ps1 -Gpu` for an NVIDIA GPU, then use `--provider decider --mode live`. The repo builds its own thin Docker image and defaults to `Mapika/decider-4b`; see [`docs/DECIDER.md`](docs/DECIDER.md).
- **Clef (Cloudflare, hosted or local).** For the hosted Workers AI path, set `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN`, then use `--provider clef --mode live`; no hardware is needed, but the state is sent to Cloudflare.
  For the local path, run `.\scripts\clef-up.ps1 -Gpu` on a 24 GB+ NVIDIA GPU and set `CLEF_BASE_URL=http://127.0.0.1:8012`, which takes precedence over the Cloudflare credentials.
  See [`docs/CLEF.md`](docs/CLEF.md).
- **Jev (hosted).** Set a TypeSafe API key in `TYPESAFE_API_KEY` and use `--provider jev --mode live`. The client sends `POST https://api.typesafe.ai/v1/systemone` and defaults to `JEV_MODEL=jev-latest`.

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

Run one level against the deterministic mock:

```bash
dotnet run --project src/TenLevels.Jev -- --level 5 --mode mock
```

Run all non-Copilot levels:

```bash
dotnet run --project src/TenLevels.Jev -- --all --mode mock
```

Run against a local Laya container (after `./scripts/laya-up.sh`):

```bash
dotnet run --project src/TenLevels.Jev -- --level 4 --provider laya --mode live
```

Run against a local Decider container (after `./scripts/decider-up.sh` or `.\scripts\decider-up.ps1`):

```bash
export DECIDER_BASE_URL=http://127.0.0.1:8011
dotnet run --project src/TenLevels.Jev -- --level 4 --provider decider --mode live
```

PowerShell:

```powershell
$env:DECIDER_BASE_URL = "http://127.0.0.1:8011"
dotnet run --project src/TenLevels.Jev -- --level 4 --provider decider --mode live
```

Run against hosted Clef on Cloudflare Workers AI:

```bash
export CLOUDFLARE_ACCOUNT_ID="..." CLOUDFLARE_API_TOKEN="..."
dotnet run --project src/TenLevels.Jev -- --level 4 --provider clef --mode live
```

PowerShell:

```powershell
$env:CLOUDFLARE_ACCOUNT_ID = "..."; $env:CLOUDFLARE_API_TOKEN = "..."
dotnet run --project src/TenLevels.Jev -- --level 4 --provider clef --mode live
```

For a local Clef container (after `./scripts/clef-up.sh --gpu` or `.\scripts\clef-up.ps1 -Gpu`), set `CLEF_BASE_URL=http://127.0.0.1:8012` instead.

Run against the hosted Jev API:

```bash
export TYPESAFE_API_KEY="..."
dotnet run --project src/TenLevels.Jev -- --level 4 --provider jev --mode live
```

PowerShell equivalent:

```powershell
$env:TYPESAFE_API_KEY = "..."
dotnet run --project src/TenLevels.Jev -- --level 4 --provider jev --mode live
```

`--provider` defaults to `DECISION_PROVIDER`, then to `laya` when `LAYA_BASE_URL` is set, then to `decider` when `DECIDER_BASE_URL` is set, then to `clef` when `CLEF_BASE_URL` is set, and to `jev` otherwise (Cloudflare credentials alone never select Clef). `--mode` defaults to `mock`. The older `--jev <mock|live>` spelling is still accepted as an alias for `--mode`.

## Run the Microsoft Agent Framework + Copilot levels

Once GitHub Copilot CLI is installed and authenticated:

```bash
dotnet run --project src/TenLevels.Jev -- --level 6 --mode mock
dotnet run --project src/TenLevels.Jev -- --level 7 --mode mock
dotnet run --project src/TenLevels.Jev -- --level 10 --mode mock
```

Use `--mode live` instead, with whichever `--provider` you configured.

These examples deliberately give the Agent Framework agent only decision function tools. They do **not** grant its built-in shell/file/URL permissions. That keeps the lesson focused and prevents the MAF sample from becoming a second, hidden execution-policy surface.

One interaction is worth knowing before you adapt this: **the repository hook also sees the tool calls the Agent Framework agent makes**, including the `jev_noul` / `jev_choice` / `jev_score` functions the host registered itself. The tools are named after the selected provider, so the `laya_*`, `decider_*`, and `clef_*` spellings appear too, and the gate treats all twelve names as non-mutating after the hard-deny and credential checks have already run. Without that, the repo's own hook denies the agent's own decision calls and levels 6, 7, and 10 fail with a fail-closed hook error.

## Run the native GitHub Copilot CLI hook

The checked-in `.github/hooks/jev-policy.json` registers a `preToolUse` command hook. GitHub Copilot sends the proposed tool name and arguments to the .NET hook adapter before execution.

On Windows the hook entry invokes the wrapper as `-NoProfile -ExecutionPolicy Bypass -File`. A default Windows execution policy refuses to load an unsigned `.ps1`, the hook then exits non-zero, and because `preToolUse` is fail-closed that silently denies **every** tool call in the session.

Choose a mode **before starting/restarting Copilot CLI**, because repository hook configuration is loaded when the CLI starts.

### Safe demo: local mock

macOS/Linux:

```bash
export DECISION_MODE=mock
copilot
```

PowerShell:

```powershell
$env:DECISION_MODE = "mock"
copilot
```

Try prompts that cause a read, a repository edit/package change, and an obviously destructive command. The demo policy is designed to show all three outcomes:

```text
allow  -> low-risk/read-only fast path or high-confidence low risk
ask    -> ambiguous/consequential mutation; Copilot asks the person
deny   -> deterministic hard hazard or sufficiently strong unsafe classification from the model
```

### Live hook, local Laya

macOS/Linux:

```bash
./scripts/laya-up.sh
export DECISION_PROVIDER=laya DECISION_MODE=live LAYA_BASE_URL=http://127.0.0.1:8010
copilot
```

PowerShell:

```powershell
./scripts/laya-up.ps1
$env:DECISION_PROVIDER = "laya"; $env:DECISION_MODE = "live"; $env:LAYA_BASE_URL = "http://127.0.0.1:8010"
copilot
```

### Live hook, local Decider

macOS/Linux:

```bash
./scripts/decider-up.sh
export DECISION_PROVIDER=decider DECISION_MODE=live DECIDER_BASE_URL=http://127.0.0.1:8011
copilot
```

PowerShell:

```powershell
.\scripts\decider-up.ps1
$env:DECISION_PROVIDER = "decider"; $env:DECISION_MODE = "live"; $env:DECIDER_BASE_URL = "http://127.0.0.1:8011"
copilot
```

See [`docs/DECIDER.md`](docs/DECIDER.md) for the Windows GPU option and model sizing.

### Live hook, hosted Clef

macOS/Linux:

```bash
export DECISION_PROVIDER=clef DECISION_MODE=live
export CLOUDFLARE_ACCOUNT_ID="..." CLOUDFLARE_API_TOKEN="..."
copilot
```

PowerShell:

```powershell
$env:DECISION_PROVIDER = "clef"; $env:DECISION_MODE = "live"
$env:CLOUDFLARE_ACCOUNT_ID = "..."; $env:CLOUDFLARE_API_TOKEN = "..."
copilot
```

This sends each gated tool call's state to Cloudflare.
To keep it on the machine, start the local container with `./scripts/clef-up.ps1 -Gpu` and set `CLEF_BASE_URL=http://127.0.0.1:8012`, which wins over the Cloudflare credentials.
See [`docs/CLEF.md`](docs/CLEF.md) for both paths and the hardware the local one needs.

### Live hook, hosted Jev

```bash
export DECISION_PROVIDER=jev DECISION_MODE=live
export TYPESAFE_API_KEY="..."
copilot
```

### No network call at all

`DECISION_MODE=off` is the hook default. In that mode, deterministic hard-deny rules still apply, known read-only tools still pass, and other mutations become `ask`.

`DECISION_MODE=auto` goes live only when the selected provider is configured (a key for Jev, a base URL for Laya or Decider, and for Clef either `CLEF_BASE_URL` or both Cloudflare variables); otherwise it behaves like `off`. The earlier `JEV_MODE` name is still read when `DECISION_MODE` is unset.

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
PASS  [jev] clef-tool -> allow
PASS  [jev] curl-pipe-shell -> deny
PASS  [jev] decider-tool -> allow
PASS  [jev] dotnet-format -> ask
PASS  [jev] edit -> ask
PASS  [jev] force-push -> deny
PASS  [jev] force-with-lease -> ask
PASS  [jev] jev-tool-with-secret -> ask
PASS  [jev] jev-tool -> allow
PASS  [jev] laya-tool -> allow
PASS  [jev] read -> allow
PASS  [jev] secret-read -> ask
...
```

Every sample is replayed against all four providers (`jev`, `laya`, `decider`, `clef`) with the same expected decision, because switching provider must change the reported reason and nothing else.

Or pipe an individual sample payload:

```bash
DECISION_MODE=mock ./scripts/jev-hook.sh < samples/hooks/edit.json
```

The hook writes exactly one final JSON decision object to stdout, for example:

```json
{"permissionDecision":"ask","permissionDecisionReason":"Laya did not clear the confidence threshold; require human approval."}
```

## Use the Copilot skill and custom agent

The repository includes a native Copilot skill at `.github/skills/jev-decisions/SKILL.md`. Copilot can select it when a task fits its description, or you can ask for it explicitly in a prompt with `/jev-decisions`.

The custom agent is `.github/agents/jev-engineer.agent.md`. In Copilot CLI you can select it with `/agent`, or launch it directly:

```bash
copilot --agent=jev-engineer --prompt "Review this change and use Jev only if a bounded semantic decision would help."
```

The agent profile does not pre-approve shell access. The repository hook remains in the normal Copilot tool lifecycle.

## Why the policy is split this way

A System-One model is useful here for **semantic uncertainty**: "is this action security-sensitive?", "which bounded route best fits?", or "where on this explicit risk rubric does this land?" It should not replace facts that C# code, Git, a parser, a compiler, or tests can determine exactly.

So `CopilotToolGate` applies layers in this order:

1. deterministic hard-deny patterns for explicitly destructive commands;
2. a deterministic read-only fast path;
3. optional `Choice(allow, ask, deny)` classification by the selected provider;
4. explicit code-owned probability/confidence thresholds;
5. human `ask` for everything that does not clear the threshold.

The important design principle is **the model advises; code authorizes**.

Step 4 gates on `max(p)` and never on the provider's own `confidence` field, because that field has provider-specific semantics: Laya uses normalized entropy, Jev Choice and Decider Choice use the TypeSafe formula, Decider Score uses its score-distance formula, and Clef reports `max(p)` itself. A threshold carried across could silently loosen or tighten. See [`docs/LAYA.md`](docs/LAYA.md), [`docs/DECIDER.md`](docs/DECIDER.md), and [`docs/CLEF.md`](docs/CLEF.md).

## Hook failure semantics you should know

GitHub's current Copilot hook contract matters for security design:

- A `preToolUse` command hook that crashes or returns a non-zero exit is fail-closed and denies the tool call.
- A hook **timeout is fail-open to Copilot's normal permission flow**, not an automatic deny.
- Under Copilot cloud agent there is no person to answer `ask`, so `ask` is treated as `deny`.

Because a timeout is the one failure mode that fails open, the hook enforces its **own** deadline (`DECISION_HOOK_DEADLINE_MS`, default 4000 ms) below the hook's `timeoutSec` of 8, so a slow decision returns an explicit `ask` instead of letting Copilot fall back to its default flow. The live HTTP call is additionally capped at two seconds - which matters most on a cold local model container, whose first request may load a large checkpoint - and ordinary provider/client exceptions become an explicit `ask` with exit code zero. See [`docs/SECURITY.md`](docs/SECURITY.md) before adapting the pattern to production.

## Wire shape used here

All four providers accept one `state`, an optional model name, and named typed questions at `POST /v1/systemone` (hosted Clef at Cloudflare's Workers AI route, inside Cloudflare's response envelope). This repo implements the three primitives:

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

The `model` field is omitted for Laya, which lets its router pick a checkpoint per request and report the choice back in a `routing` block, and for Decider, whose server process owns the model selection through `DECIDER_MODEL`. Clef, by contrast, rejects a request without one, so the client always sends `"model": "clef-flash"` (or `clef` when `CLEF_MODEL=clef`). The client is intentionally tiny and lives in [`src/Jev.Core/SystemOneHttpClient.cs`](src/Jev.Core/SystemOneHttpClient.cs), so the wire contract is easy to inspect.

## Suggested demo script

For a 10-15 minute walkthrough:

1. Run Levels 1-4 in mock mode to introduce Noul/Choice/Score and parallel questions.
2. Run Level 5 to show that policy stays in C#.
3. Re-run Level 4 with `--provider laya --mode live` then `--provider decider --mode live`, and then `--provider clef --mode live`: same code and DTOs, four interchangeable engines, and provider-specific `confidence` next to the portable `AnswerConfidence`.
4. Run Level 7 with an authenticated Copilot runtime to show the decision primitives as MAF function tools.
5. Run `./scripts/test-hook.sh` to show the exact native `preToolUse` JSON contract.
6. Start `DECISION_MODE=mock copilot`, make Copilot inspect a file, edit a file, and propose a force push; point out `allow`, `ask`, and `deny`.
7. End on Level 10 plus `/jev-decisions`: the agent learns when a fast typed decision is useful, without turning the model into an unconstrained planner.

## Production hardening ideas

This repo intentionally keeps the sample readable. The deterministic rules in `src/Jev.Core/RiskHeuristics.cs` are anchored regular expressions rather than substring tests, and each one is pinned by a self-test in both directions, but a regex still is not a shell parser: `rm $VAR` and base64-encoded payloads are outside its reach. Before using the pattern as organizational policy, consider signed/versioned policy distribution, real shell/command-line parsing, dedicated secret redaction, audit/event logging, latency and error telemetry, organization-specific allow/deny rules, sandboxing, policy tests derived from real tool traces, and a clear decision on whether a remote call is permitted for the data being evaluated - which is a strong argument for a self-hosted Laya, Decider, or local Clef path. Thresholds here are illustrative: re-fit them against the provider and checkpoint you actually deploy.

## Sources and compatibility

See [`docs/SOURCES.md`](docs/SOURCES.md). The implementation was checked against the GitHub Copilot CLI hook documentation, the GitHub Copilot skill/custom-agent documentation, the Microsoft Agent Framework GitHub Copilot provider docs, the current TypeSafe OpenAPI document, Laya's HTTP API documentation and server source, Decider's model/package/server documentation, and the current NuGet package metadata while this demo was assembled in September 2026.
The Clef integration was checked against Cloudflare's Workers AI model page, its input and output JSON schemas, and the Hugging Face model card in October 2026.

MIT licensed. This is an independent demo and is not an official TypeSafe, Laya, Decider, Cloudflare, GitHub, or Microsoft project.
