---
"@agon_agents/cli": patch
---

Improve CLI auth session UX by adding silent token renewal paths and safer retry behavior.

This update persists auth session metadata for non-interactive renewal, limits automatic auth retry to 401 responses, and improves compatibility for manual token sessions.
