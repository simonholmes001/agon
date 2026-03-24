---
"@agon_agents/cli": minor
---

Add per-user runtime profile support for model routing and provider API keys.

Highlights:
- Add `agon command` management flow (`show`, `onboard`, `set-model`, `set-key`, `rotate-key`, `delete-key`, `recover-key`).
- Scope secrets by user profile and support legacy key migration.
- Wire runtime profile into session start/shell/follow-up calls with explicit missing-key guidance.
- Improve provider compatibility by normalizing `gemini` to canonical `google`.
- Expand tests for scoped key lifecycle, runtime headers, user scope parsing, and model profile persistence.
