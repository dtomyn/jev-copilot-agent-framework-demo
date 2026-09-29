# Agent notes

Build with .NET 10 (pinned in `global.json`). The solution is intentionally divided into:

- `Jev.Core`: System-One DTOs/client, provider selection, deterministic mock, and policy gate.
- `TenLevels.Jev`: ten incremental examples.
- `Jev.CopilotHook`: stdin/stdout adapter for GitHub Copilot CLI `preToolUse`.
- `Jev.WebUi`: browser front end for presenting the levels (source pane plus a run panel per level).
- `Jev.Core.SelfTests`: dependency-free smoke tests.

Do not collapse the levels into one abstraction; the progression is part of the demo. A change to hook policy should be accompanied by a self-test and a hook sample where possible.

Two decision providers are supported, TypeSafe Jev and open-source Laya, and `laya-serve` speaks the same `POST /v1/systemone` contract. Keep that difference confined to `SystemOneEndpoint` in `src/Jev.Core/DecisionProviders.cs`: policy code must stay provider-agnostic. See `docs/LAYA.md`.

Build invariants worth preserving:

- `CopilotToolGate` thresholds on `AnswerConfidence` (`max(p)`), never on the wire `confidence` field. Jev defines that field as `(n*p_max - 1)/(n - 1)` and Laya as normalized entropy, so a threshold read off it means two different things. A self-test asserts that switching provider changes no gate decision, and another asserts the mock reproduces both formulas.
- The three Agent Framework function tools are named after the selected provider (`jev_*` or `laya_*`), and `CopilotToolGate` allow-lists both prefixes. The hook cannot know which provider another process chose, so dropping either set breaks levels 6, 7, and 10 for that provider.
- `DECISION_MODE` and `DECISION_HOOK_DEADLINE_MS` fall back to the older `JEV_MODE` and `JEV_HOOK_DEADLINE_MS` when unset. Keep the fallback: an existing shell session or `.env` must not start failing closed.
- `global.json` pins the SDK and `NuGet.config` pins a single feed. Central Package Management fails (NU1507) if a machine-level feed leaks in, and `TreatWarningsAsErrors` turns that into a build break.
- `TenLevels.Jev` is the only project that needs `registry.npmjs.org` at build time. Keep it buildable last so the hook and self-tests still work offline. `Jev.WebUi` must not reference it for the same reason: the UI is how the demo starts, and an npm download in front of that is a demo that does not start. The UI reaches the Copilot-backed levels by spawning `dotnet run --project src/TenLevels.Jev` instead.
- `Jev.WebUi` reads level names, summaries and source text out of the repository at runtime rather than restating them. A level renamed in `TenLevels.Jev` renames itself in the UI; a hand-copied catalogue would not.
- Presenter notes live in `docs/tutorial.html` only. `TutorialNotes` extracts the `<div class="notes" data-level="...">` blocks and the UI renders them, so a note written once shows up in both surfaces. Adding a note to a level means adding `data-level` to its block, not copying prose into the UI. The four level sub-headings keep their `id="level01".."level04"` anchors, and each level section keeps its `a.runlive` deep link into the UI, because those are the links in the other direction.
- `GateResult.Stage` exists so an explanation of a decision reports which gate layer fired instead of re-deriving it. Anything that needs to explain a gate result reads that, never the reason string and never a second copy of the heuristics.
- Deterministic rules live in `src/Jev.Core/RiskHeuristics.cs` as anchored regexes. Every rule needs a self-test in both directions: a command that must match and a benign one that must not.
- The Copilot runtime spawned by the Agent Framework levels loads `.github/hooks/jev-policy.json` too, so the hook gates the agent's own decision function tools. `CopilotToolGate` allows them by name; removing that breaks levels 6, 7, and 10.
- The Windows hook entry must keep `-ExecutionPolicy Bypass -File`. An unsigned `.ps1` refused by the execution policy exits non-zero, and `preToolUse` is fail-closed, so every tool call in the session is denied.
- The hook must always exit 0 and print exactly one JSON object. A non-zero exit denies every tool call; a hook timeout fails open, which is why the adapter enforces its own shorter deadline.
