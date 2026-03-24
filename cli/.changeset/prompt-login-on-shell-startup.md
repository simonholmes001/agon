---
"@agon_agents/cli": patch
---

Enforce token-first startup for backend-interacting CLI commands:

- `agon shell` and `agon start` now require a configured bearer token by default
- when no token exists, CLI exits with explicit setup guidance (`agon login`, `agon login --status`)
- local development bypass remains available with `AGON_ALLOW_ANONYMOUS=true`
