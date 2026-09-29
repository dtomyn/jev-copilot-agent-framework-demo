# Repository instructions for GitHub Copilot

This repository demonstrates Jev + GitHub Copilot CLI + Microsoft Agent Framework in .NET.

- Keep Jev questions bounded: Noul, Choice, or Score.
- Prefer deterministic evidence (read code, compile, test, lint) whenever it can answer the question.
- Never treat a Jev result as direct authority to execute a dangerous command. Code-owned policy and the Copilot permission flow remain authoritative.
- Do not weaken `.github/hooks/jev-policy.json`, `scripts/jev-hook.*`, or `CopilotToolGate` merely to make a command pass.
- Do not put credentials, access tokens, private keys, or secrets into Jev state.
- Keep examples cross-platform where practical and preserve both Bash and PowerShell hook paths.
- This is a learning repo: keep each numbered level focused on one new idea.
