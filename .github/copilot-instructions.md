# Repository instructions for GitHub Copilot

This repository demonstrates typed System-One decisions (TypeSafe Jev, open-source Laya, or open-source Decider) + GitHub Copilot CLI + Microsoft Agent Framework in .NET.

- Keep decision questions bounded: Noul, Choice, or Score.
- Prefer deterministic evidence (read code, compile, test, lint) whenever it can answer the question.
- Never treat a model result as direct authority to execute a dangerous command. Code-owned policy and the Copilot permission flow remain authoritative.
- Do not weaken `.github/hooks/jev-policy.json`, `scripts/jev-hook.*`, or `CopilotToolGate` merely to make a command pass.
- Keep all three providers working. Provider differences belong in `SystemOneEndpoint`, not in policy code, and thresholds read `AnswerConfidence`, not the wire `confidence` field.
- Do not put credentials, access tokens, private keys, or secrets into the decision state, local provider or not.
- Keep examples cross-platform where practical and preserve both Bash and PowerShell hook paths.
- This is a learning repo: keep each numbered level focused on one new idea.
