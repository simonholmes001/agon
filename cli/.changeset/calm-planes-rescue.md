---
"@agon_agents/cli": patch
---

Enable true "attach anytime" behavior by auto-creating a session when none is active, and by recovering from stale session pointers when upload returns SESSION_NOT_FOUND.
