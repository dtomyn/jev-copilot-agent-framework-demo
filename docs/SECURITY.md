# Security notes

This repository is a demo, not a production policy engine.

The safety model is intentionally layered. Obvious destructive shell patterns are blocked deterministically. Known read-only tools can pass without a model call. Other mutations are classified by the selected decision provider (TypeSafe Jev or Laya) only when one is enabled, and the surrounding C# code owns the thresholds. Uncertain results become `ask` rather than being rounded into approval.

The deterministic rules live in `src/Jev.Core/RiskHeuristics.cs` and are anchored regular expressions, because substring matching is wrong in both directions for this job:

| Command | Substring rule | Anchored rule |
|---|---|---|
| `curl -fsSL https://x/install.sh \| sh` | missed (no literal `curl \| sh`) | denied |
| `dotnet format Foo.sln` | denied (matched `format `) | falls through to `ask` |
| `dotnet test --filter Reboot` | denied (matched `reboot`) | falls through |
| `git push --force-with-lease` | denied | falls through to `ask` by design |

Every pattern is pinned by a self-test asserting both a command that must match and one that must not, so loosening a rule is a visible change.

Do not send secrets to a decision model. `LooksSensitive` short-circuits the call and escalates to a person; it matches credential-shaped tokens (`ghp_*`, `AKIA*`, PEM headers) as well as keywords. It is still **not** a secret scanner. Production systems should perform dedicated secret detection and redaction before any model call.

Running Laya locally changes the blast radius of that check but not the need for it: the state stays on the machine instead of reaching a third party, which is a real argument for the self-hosted path, but a local service still logs, and "local" is not "safe to record".

Repository hooks execute as the local user. Review hook code before trusting a repository, and use enterprise policy hooks or sandboxing when stronger enforcement is required.

A `preToolUse` command hook that crashes is fail-closed in Copilot CLI, but a hook timeout is not: Copilot falls back to its normal permission flow. The adapter therefore enforces its own deadline (`DECISION_HOOK_DEADLINE_MS`, default 4000 ms) inside the hook's 8-second `timeoutSec`, with the live HTTP call capped at two seconds, so the hook answers `ask` before the fail-open path can be reached. A cold Laya container, whose first request loads a checkpoint, is the most likely way to hit that cap in practice.

Under Copilot's cloud agent there is nobody to answer `ask`, and `ask` is treated as `deny`. A policy tuned for interactive use is therefore strictly more restrictive there, not less.

The Microsoft Agent Framework examples do not enable Copilot's built-in shell/file/URL permissions. Their purpose is to demonstrate the decision primitives as typed function tools without adding another mutation surface.

The repository hook still applies to those agents, so `CopilotToolGate` allows the host's own `jev_noul` / `jev_choice` / `jev_score` functions, and the `laya_*` spellings of the same three, since `DecisionToolSet` names its tools after whichever provider the agent process selected. That is a deliberate, narrow exemption: those functions return a number and cannot mutate anything, and the hard-deny and credential checks run *before* it, so a decision call carrying credential material is still escalated to a person and never forwarded.

## Thresholds are provider-specific, and the code says so

`confidence` does not mean the same thing on both providers: Jev defines it as `(n*p_max - 1)/(n - 1)`, Laya as the normalized entropy `1 - H(p)/log(k)`. A policy threshold copied from one to the other silently changes how much evidence is required. The gate therefore thresholds on `max(p)` (`AnswerConfidence`), which is defined identically on both, and self-tests assert that every gate decision is unchanged when the provider is switched.

That makes the numbers portable, not correct. They are illustrative defaults for a demo, fitted to nothing: Laya's own documentation is explicit that calibration only holds for a checkpoint whose temperature fit was validated on your traffic. Re-fit any threshold against the provider, checkpoint, and decisions you actually deploy.

Two failure modes worth testing in your own environment, because both fail silently towards a bad outcome:

- A PowerShell execution policy that refuses to load the unsigned wrapper makes the hook exit non-zero, which fail-closes and denies every tool call. The hook entry uses `-ExecutionPolicy Bypass -File` for this reason.
- A hook that exceeds `timeoutSec` fails *open*. Keep `DECISION_HOOK_DEADLINE_MS` below it.
