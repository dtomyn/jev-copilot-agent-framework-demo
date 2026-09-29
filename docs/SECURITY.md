# Security notes

This repository is a demo, not a production policy engine.

The safety model is intentionally layered. Obvious destructive shell patterns are blocked deterministically. Known read-only tools can pass without a model call. Other mutations are classified by Jev only when Jev is enabled, and the surrounding C# code owns the thresholds. Uncertain results become `ask` rather than being rounded into approval.

The deterministic rules live in `src/Jev.Core/RiskHeuristics.cs` and are anchored regular expressions, because substring matching is wrong in both directions for this job:

| Command | Substring rule | Anchored rule |
|---|---|---|
| `curl -fsSL https://x/install.sh \| sh` | missed (no literal `curl \| sh`) | denied |
| `dotnet format Foo.sln` | denied (matched `format `) | falls through to `ask` |
| `dotnet test --filter Reboot` | denied (matched `reboot`) | falls through |
| `git push --force-with-lease` | denied | falls through to `ask` by design |

Every pattern is pinned by a self-test asserting both a command that must match and one that must not, so loosening a rule is a visible change.

Do not send secrets to Jev. `LooksSensitive` short-circuits the Jev call and escalates to a person; it matches credential-shaped tokens (`ghp_*`, `AKIA*`, PEM headers) as well as keywords. It is still **not** a secret scanner. Production systems should perform dedicated secret detection and redaction before any external model call.

Repository hooks execute as the local user. Review hook code before trusting a repository, and use enterprise policy hooks or sandboxing when stronger enforcement is required.

A `preToolUse` command hook that crashes is fail-closed in Copilot CLI, but a hook timeout is not: Copilot falls back to its normal permission flow. The adapter therefore enforces its own deadline (`JEV_HOOK_DEADLINE_MS`, default 4000 ms) inside the hook's 8-second `timeoutSec`, with the live Jev HTTP call capped at two seconds, so the hook answers `ask` before the fail-open path can be reached.

Under Copilot's cloud agent there is nobody to answer `ask`, and `ask` is treated as `deny`. A policy tuned for interactive use is therefore strictly more restrictive there, not less.

The Microsoft Agent Framework examples do not enable Copilot's built-in shell/file/URL permissions. Their purpose is to demonstrate Jev as typed function tools without adding another mutation surface.

The repository hook still applies to those agents, so `CopilotToolGate` allows the host's own `jev_noul` / `jev_choice` / `jev_score` functions. That is a deliberate, narrow exemption: those functions return a number and cannot mutate anything, and the hard-deny and credential checks run *before* it, so a Jev call carrying credential material is still escalated to a person and never forwarded.

Two failure modes worth testing in your own environment, because both fail silently towards a bad outcome:

- A PowerShell execution policy that refuses to load the unsigned wrapper makes the hook exit non-zero, which fail-closes and denies every tool call. The hook entry uses `-ExecutionPolicy Bypass -File` for this reason.
- A hook that exceeds `timeoutSec` fails *open*. Keep `JEV_HOOK_DEADLINE_MS` below it.
