---
name: jev-decisions
description: Use a typed System-One decision engine (TypeSafe Jev, local Laya, or local Decider) for bounded, fast semantic judgments (yes/no probability, one-of-N choice, or ordered score) during coding work. Use when uncertainty is semantic rather than something that can be inspected or calculated deterministically.
---

# Typed decisions

Use a decision engine only for a narrow judgment whose complete answer space can be written down before the call. Three providers are interchangeable here: hosted TypeSafe Jev, open-source Laya running locally (`docs/LAYA.md`), and open-source Decider running locally (`docs/DECIDER.md`). The primitives and guardrails below are provider-independent.

## Pick the primitive

- **Noul**: one yes/no proposition; result is the probability of yes/true.
- **Choice**: exactly one member of a bounded set; preserve the returned probabilities.
- **Score**: an ordered rubric; each criterion is a concrete level beginning at zero.

Gate on `answerConfidence` (`max(p)`), not on `confidence`: that field has provider-specific semantics (including Laya entropy and Decider score confidence), so a threshold read off it is not portable.

## Guardrails

1. Prefer deterministic inspection, tests, parsers, compilers, linters, and arithmetic over Jev when they can answer the question directly.
2. Keep the input state limited to evidence relevant to the judgment. Do not send secrets or credentials.
3. Do not ask the model to write code, execute commands, invent policy, or make open-ended plans.
4. The caller owns thresholds and policy. Treat the result as evidence, not authorization.
5. Preserve uncertainty. If confidence is insufficient for the code-owned threshold, escalate to a person rather than forcing a decision.
6. For tool execution, the repository's `preToolUse` hook remains authoritative. Do not try to bypass or weaken it.

## This repository

The .NET implementation is in `src/Jev.Core`; provider selection is in `DecisionProviders.cs` and the shared client in `SystemOneHttpClient.cs`. The progression is in `src/TenLevels.Jev/Levels`. Start with Level 01 and work upward. For native GitHub Copilot CLI hook behavior, inspect `.github/hooks/jev-policy.json` and `src/Jev.CopilotHook`.
