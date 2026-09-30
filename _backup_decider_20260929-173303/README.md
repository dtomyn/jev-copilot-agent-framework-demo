# Typed Decisions for Agents: Jev + Laya + .NET + GitHub Copilot

This is an interactive learning project about a small but important idea in agentic software:

> **A general-purpose agent does not need to make every judgment itself.**
>
> When the question is narrow and the answer shape is known, a fast decision engine can return typed evidence and ordinary code can decide what happens next.

The project demonstrates that idea with two compatible System One decision engines:

- **[TypeSafe Jev](https://typesafe.ai/)** — hosted.
- **[Laya](https://github.com/NandhaKishorM/laya)** — open source and runnable locally.

It starts with a single yes/no judgment and builds, one level at a time, toward a real **GitHub Copilot CLI `preToolUse` guardrail** and an agent that can decide for itself when a bounded typed judgment would be useful.

The examples are written in **C# / .NET 10** and use **Microsoft Agent Framework** for the Copilot-backed levels.

---

## Start here: run the interactive demo

You only need the **.NET 10 SDK** to explore the core demo. The default experience uses a deterministic local mock, so there is **no API key, model download, Docker container, or network call required**.

### macOS / Linux

```bash
./scripts/webui.sh
```

### Windows PowerShell 7+

```powershell
./scripts/webui.ps1
```

Then open:

**http://127.0.0.1:5088**

The browser app is the best way to learn the project. For each level it shows:

- the **actual checked-in source code** on one side;
- an **editable live example** on the other;
- the request, typed answer, probabilities, and policy result;
- short **“what to notice”** explanations pulled directly from the tutorial.

Change the state, wording, choices, score rubric, or threshold and run the example again. The point is to see how a small change in the question or evidence changes the decision.

> **Want the guided explanation first?** Open [`docs/tutorial.html`](docs/tutorial.html), or use the **Tutorial** button in the running app.

---

## The whole project in 60 seconds

Every example follows the same basic loop:

```text
state or context
      │
      ▼
ask a narrow typed question
      │
      ▼
Jev or Laya returns probabilities / a typed answer
      │
      ▼
your C# policy interprets that evidence
      │
      ▼
allow • ask a human • deny • route • score • continue
```

The important boundary is this:

> **The decision model supplies evidence; your code owns the action.**

Jev or Laya can answer questions such as:

- “Does this command look destructive?”
- “Which of these three routes best fits this ticket?”
- “Where does this change fall on an explicit risk scale?”

They are **not** being asked to become the whole agent, execute the command, edit the repository, or invent an unconstrained plan.

That is the central idea this repo builds on from Level 1 through Level 10.

---

## Three primitives are enough to understand the demo

| Primitive | Plain-English question | Typical result |
|---|---|---|
| **Noul** | “Is this true?” | Probability of yes |
| **Choice** | “Which named option fits best?” | One label plus option probabilities |
| **Score** | “Where does this fall on this ordered scale?” | A position/distribution over the rubric |

A useful rule of thumb: **if you cannot describe the answer shape before the model runs, the problem may be too open-ended for this primitive.** Break it into smaller judgments or let normal code / the agent handle it another way.

Both Jev and Laya are used through the same `POST /v1/systemone` request shape in this project.

---

## The ten levels

The levels are intentionally incremental. Each one adds one idea rather than replacing everything that came before it.

| Level | Idea | What you learn |
|---:|---|---|
| **01** | Smart if-statement | A Noul probability can drive an ordinary C# branch. |
| **02** | Typed routing | A bounded Choice is cleaner than asking for free-form routing prose. |
| **03** | Ordered risk | A Score places state on an explicit ordered rubric. |
| **04** | Parallel judgments | Noul, Choice, and Score can evaluate the same state together. |
| **05** | Code owns policy | Hard rules and code-owned thresholds wrap model evidence. |
| **06** | One decision as an agent tool | Microsoft Agent Framework exposes a typed decision function to Copilot. |
| **07** | The full decision toolbelt | Copilot receives Noul, Choice, and Score as callable tools. |
| **08** | Real `preToolUse` hook | The same gate participates in GitHub Copilot CLI's native tool lifecycle. |
| **09** | Confidence-aware escalation | Clear cases can proceed or stop; ambiguous cases become a human `ask`. |
| **10** | Agentic typed decisions | The agent decides when a bounded decision-engine call is useful. |

The progression is not “more AI.” It is **better placement of a small decision primitive**.

For the full walkthrough, examples, and explanations, see [`docs/tutorial.html`](docs/tutorial.html).

---

## Jev and Laya in this project

The demo deliberately supports both engines so you can see the architectural pattern separately from the provider.

| | TypeSafe Jev | Laya |
|---|---|---|
| Deployment | Hosted service | Open source, local service |
| API used here | `POST /v1/systemone` | Jev-compatible `POST /v1/systemone` |
| Authentication | `TYPESAFE_API_KEY` | None by default locally; optional `LAYA_API_KEY` |
| Model selection | `JEV_MODEL`, default `jev-latest` | Router by default, or `LAYA_MODEL` |
| Data path | Sent to hosted API | Can stay on your machine |

The provider is selected at the boundary. The policy code is intentionally provider-agnostic.

One subtle difference matters: the providers do **not** define their returned `confidence` field the same way. The gate therefore authorizes on the probability mass of the selected answer (`AnswerConfidence` / `max(p)`), not on a provider-specific confidence formula. See [`docs/LAYA.md`](docs/LAYA.md) for the details.

---

## Try a real decision engine

### Local Laya

Laya is the easiest way to turn the demo from mock mode into a real local model without sending the judged state to a hosted service.

The helper scripts expect an external Laya compose checkout configured as described in [`docs/LAYA.md`](docs/LAYA.md).

Start Laya and the web UI together:

```bash
./scripts/webui.sh --laya
```

```powershell
./scripts/webui.ps1 -Laya
```

Or start only the local service:

```bash
./scripts/laya-up.sh
```

```powershell
./scripts/laya-up.ps1
```

The default local endpoint used by this repo is `http://127.0.0.1:8010`.

### Hosted TypeSafe Jev

Set a TypeSafe API key before starting the app or running the levels:

```bash
export TYPESAFE_API_KEY="..."
```

```powershell
$env:TYPESAFE_API_KEY = "..."
```

Then choose **Jev + Live** in the web UI, or run a level directly:

```bash
dotnet run --project src/TenLevels.Jev -- --level 4 --provider jev --mode live
```

The web UI also has a masked Jev-key field in its **Config** dialog. The value is kept in memory for that process and is not written to disk.

---

## Run the levels from the terminal

List the levels:

```bash
dotnet run --project src/TenLevels.Jev -- --list
```

Run one level with the deterministic mock:

```bash
dotnet run --project src/TenLevels.Jev -- --level 5 --mode mock
```

Run the progression:

```bash
dotnet run --project src/TenLevels.Jev -- --all --mode mock
```

Run Level 4 against local Laya:

```bash
dotnet run --project src/TenLevels.Jev -- --level 4 --provider laya --mode live
```

### Decision modes

| Mode | Meaning |
|---|---|
| `mock` | Deterministic local simulator; best for learning and demos |
| `live` | Call the selected Jev or Laya provider |
| `off` | Hook uses deterministic rules only; unresolved mutations become `ask` |
| `auto` | Hook uses live mode only when the selected provider is configured |

---

## Levels 6, 7, and 10: GitHub Copilot + Microsoft Agent Framework

The full agent versions of these levels require an **installed and authenticated GitHub Copilot CLI**.

Build the agent-backed levels when starting the UI:

```bash
./scripts/webui.sh --include-agent-levels
```

```powershell
./scripts/webui.ps1 -IncludeAgentLevels
```

Or run them directly:

```bash
dotnet run --project src/TenLevels.Jev -- --level 6 --mode mock
dotnet run --project src/TenLevels.Jev -- --level 7 --mode mock
dotnet run --project src/TenLevels.Jev -- --level 10 --mode mock
```

These examples intentionally focus the Agent Framework integration on **decision function tools**. They do not grant the programmatic agent a second hidden set of shell/file permissions.

> `GitHub.Copilot.SDK` downloads the Copilot CLI runtime from `registry.npmjs.org` at build time on a cold cache. If that is blocked in your environment, the rest of the demo still works. See the tutorial or existing build scripts for the supported `CopilotSkipCliDownload`, mirror, and local-binary options.

---

## The real Copilot guardrail

Levels 8 and 9 are not a simulation of a guardrail. The repository contains an actual GitHub Copilot CLI `preToolUse` hook:

```text
.github/hooks/jev-policy.json
        │
        ▼
scripts/jev-hook.*
        │
        ▼
src/Jev.CopilotHook
        │
        ▼
CopilotToolGate in Jev.Core
```

The gate deliberately layers deterministic code and model judgment:

```text
1. obvious destructive pattern?  ───────────────► deny
2. looks like sensitive material? ──────────────► ask; do not send it to the model
3. known read-only tool? ───────────────────────► allow
4. otherwise ask Jev/Laya for bounded evidence
5. code-owned threshold clears the decision? ──► allow / deny
6. still ambiguous? ────────────────────────────► ask a human
```

That is why the demo's safety principle is **not** “model says safe → execute.”

Test the hook without starting Copilot:

```bash
./scripts/test-hook.sh
```

```powershell
./scripts/test-hook.ps1
```

Then, for a mock-mode Copilot session, set the mode **before starting Copilot CLI**:

```bash
export DECISION_MODE=mock
copilot
```

```powershell
$env:DECISION_MODE = "mock"
copilot
```

See [`docs/SECURITY.md`](docs/SECURITY.md) before adapting the hook pattern to real policy enforcement.

---

## Bootstrap and verify the repository

### macOS / Linux

```bash
./scripts/bootstrap.sh
```

### Windows PowerShell 7+

```powershell
./scripts/bootstrap.ps1
```

Bootstrap restores the solution, builds the core projects, runs the self-tests, replays the checked-in hook samples, and then attempts the Copilot-backed levels last.

That ordering is deliberate: a restricted npm connection should not prevent you from using the core typed-decision demo or the hook tests.

---

## Project map

```text
.
├── .github/
│   ├── agents/jev-engineer.agent.md     # custom Copilot agent
│   ├── hooks/jev-policy.json            # native preToolUse hook configuration
│   ├── skills/jev-decisions/SKILL.md    # skill that teaches Copilot when to use typed decisions
│   └── copilot-instructions.md
├── docs/
│   ├── tutorial.html                    # main guided walkthrough
│   ├── ARCHITECTURE.md                  # implementation architecture
│   ├── LAYA.md                          # local Laya setup and provider differences
│   ├── LEVELS.md                        # concise level rationale
│   ├── SECURITY.md                      # hook/security behavior and caveats
│   └── SOURCES.md                       # public references used to build the demo
├── samples/hooks/                       # sample Copilot hook payloads + expected outcomes
├── scripts/                             # bootstrap, UI, Laya, and hook helpers
├── src/
│   ├── Jev.Core/                        # DTOs, providers, mock, heuristics, policy gate
│   ├── Jev.CopilotHook/                 # stdin/stdout preToolUse adapter
│   ├── Jev.WebUi/                       # interactive browser demo
│   └── TenLevels.Jev/                   # levels 01-10
├── tests/Jev.Core.SelfTests/            # dependency-free smoke tests
├── .env.example                         # provider/runtime configuration reference
└── JevCopilotDemo.sln
```

A useful implementation detail: the web UI does not maintain a second copy of the tutorial prose. It reads the level notes from [`docs/tutorial.html`](docs/tutorial.html), so the written explanation and running demo stay connected.

---

## Where to read next

- **New to the idea?** Start with [`docs/tutorial.html`](docs/tutorial.html).
- **Want the architecture?** Read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
- **Running Laya locally?** Read [`docs/LAYA.md`](docs/LAYA.md).
- **Adapting the hook?** Read [`docs/SECURITY.md`](docs/SECURITY.md).
- **Want the source references?** Read [`docs/SOURCES.md`](docs/SOURCES.md).

---

## Before using this pattern in production

This repository is a teaching demo, not a drop-in organizational security policy.

The hard rules are intentionally readable, thresholds are illustrative, and the mock is designed for predictable demonstrations rather than benchmarking model quality. Real deployments should validate decisions against their own traffic and consider stronger command parsing, secret handling, sandboxing, audit logs, latency/error telemetry, signed policy distribution, organization-specific rules, and explicit failure behavior.

Most importantly, keep the separation shown throughout the demo:

> **Use typed models for bounded semantic uncertainty. Use deterministic software for facts and hard rules. Keep final authority in policy code.**

---

## Inspiration

The learning progression was inspired by the **“10 Levels of Jev for Agentic Engineers”** video and the associated `disler/ten-levels-of-jev` example structure. This repository adapts that progression to a .NET + GitHub Copilot workflow rather than reproducing it level-for-level.

- [10 Levels of Jev for Agentic Engineers — YouTube](https://www.youtube.com/watch?v=_U-O5lYhJ7Q)
- [TypeSafe AI / Jev](https://typesafe.ai/)
- [Laya](https://github.com/NandhaKishorM/laya)
- [Project references and compatibility notes](docs/SOURCES.md)

This is an independent demo and is not an official TypeSafe, Laya, GitHub, or Microsoft project.

## License

MIT — see [`LICENSE`](LICENSE).
