---
"@agon_agents/cli": patch
---

Fix TTY mock cycle in shell tests to use a single mock instance, preventing test isolation issues.
