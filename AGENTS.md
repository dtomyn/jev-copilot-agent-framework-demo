# Agent notes

Build with .NET 10 (pinned in `global.json`). The solution is intentionally divided into:

- `Jev.Core`: Jev DTOs/client, deterministic mock, and policy gate.
- `TenLevels.Jev`: ten incremental examples.
- `Jev.CopilotHook`: stdin/stdout adapter for GitHub Copilot CLI `preToolUse`.
- `Jev.Core.SelfTests`: dependency-free smoke tests.

Do not collapse the levels into one abstraction; the progression is part of the demo. A change to hook policy should be accompanied by a self-test and a hook sample where possible.

Build invariants worth preserving:

- `global.json` pins the SDK and `NuGet.config` pins a single feed. Central Package Management fails (NU1507) if a machine-level feed leaks in, and `TreatWarningsAsErrors` turns that into a build break.
- `TenLevels.Jev` is the only project that needs `registry.npmjs.org` at build time. Keep it buildable last so the hook and self-tests still work offline.
- Deterministic rules live in `src/Jev.Core/RiskHeuristics.cs` as anchored regexes. Every rule needs a self-test in both directions: a command that must match and a benign one that must not.
- The Copilot runtime spawned by the Agent Framework levels loads `.github/hooks/jev-policy.json` too, so the hook gates the agent's own `jev_*` function tools. `CopilotToolGate` allows those three by name; removing that breaks levels 6, 7, and 10.
- The Windows hook entry must keep `-ExecutionPolicy Bypass -File`. An unsigned `.ps1` refused by the execution policy exits non-zero, and `preToolUse` is fail-closed, so every tool call in the session is denied.
- The hook must always exit 0 and print exactly one JSON object. A non-zero exit denies every tool call; a hook timeout fails open, which is why the adapter enforces its own shorter deadline.
