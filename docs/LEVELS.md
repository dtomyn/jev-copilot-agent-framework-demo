# The ten levels

The progression intentionally mirrors the learning shape of `ten-levels-of-jev`: start with one small typed decision and end with a coding agent that knows when a System-One model is the right primitive.

Every level runs unchanged against either provider - TypeSafe Jev or a local Laya container - because the difference is confined to `SystemOneEndpoint`. Add `--provider laya` or `--provider jev` to any command; see [LAYA.md](LAYA.md).

| Level | Demo | New idea | Runtime |
|---:|---|---|---|
| 01 | Smart if-statement | Noul probability + ordinary C# threshold | .NET only |
| 02 | Typed routing | Choice over an explicit answer set | .NET only |
| 03 | Ordered risk | Score over an ordered rubric | .NET only |
| 04 | Parallel judgments | Noul + Choice + Score in one request | .NET only |
| 05 | Code owns policy | Deterministic rules + model + thresholds | .NET only |
| 06 | The engine becomes a Copilot tool | One decision `AIFunction` exposed to a Copilot-backed `AIAgent` | MAF + Copilot |
| 07 | Copilot gets the toolbelt | Agent can compose all three primitives | MAF + Copilot |
| 08 | Native `preToolUse` hook | The gate participates in Copilot CLI's actual tool lifecycle | Copilot CLI hook |
| 09 | Human escalation | `allow` / `ask` / `deny`, confidence-aware | Copilot CLI hook |
| 10 | Agent self-selects the engine | Agent decides when a bounded typed judgment is useful | MAF + Copilot + skill |

## Why levels 8 and 9 are not hidden inside Agent Framework

The repository demonstrates the exact surface your organization would operate: a GitHub Copilot CLI repository hook in `.github/hooks`. Agent Framework's Copilot provider is used where it is strongest here: composing a coding-oriented agent with typed .NET function tools.
