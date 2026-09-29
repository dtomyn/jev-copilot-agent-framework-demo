---
name: jev-decisions
description: Use Jev for bounded, fast semantic judgments (yes/no probability, one-of-N choice, or ordered score) during coding work. Use when uncertainty is semantic rather than something that can be inspected or calculated deterministically.
---

# Jev decisions

Use Jev only for a narrow judgment whose complete answer space can be written down before the call.

## Pick the primitive

- **Noul**: one yes/no proposition; result is the probability of yes/true.
- **Choice**: exactly one member of a bounded set; preserve the returned confidence and probabilities.
- **Score**: an ordered rubric; each criterion is a concrete level beginning at zero.

## Guardrails

1. Prefer deterministic inspection, tests, parsers, compilers, linters, and arithmetic over Jev when they can answer the question directly.
2. Keep the input state limited to evidence relevant to the judgment. Do not send secrets or credentials.
3. Do not ask Jev to write code, execute commands, invent policy, or make open-ended plans.
4. The caller owns thresholds and policy. Treat Jev as evidence, not authorization.
5. Preserve uncertainty. If confidence is insufficient for the code-owned threshold, escalate to a person rather than forcing a decision.
6. For tool execution, the repository's `preToolUse` hook remains authoritative. Do not try to bypass or weaken it.

## This repository

The .NET implementation is in `src/Jev.Core`. The progression is in `src/TenLevels.Jev/Levels`. Start with Level 01 and work upward. For native GitHub Copilot CLI hook behavior, inspect `.github/hooks/jev-policy.json` and `src/Jev.CopilotHook`.
