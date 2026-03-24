---
'@agon_agents/cli': patch
---

Improve first-run authentication UX by auto-discovering tenant/scope from backend `/auth/status` metadata and defaulting `agon login` to Azure CLI sign-in when auth is required.
