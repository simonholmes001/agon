---
"@agon_agents/cli": patch
---

Fix Zod v4 compatibility in config-manager.ts: replace `ZodError.errors` (removed in Zod v4) with `ZodError.issues`.
