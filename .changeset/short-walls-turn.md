---
"@agon_agents/cli": patch
---

Fix `agon login` token verification to use an auth-only endpoint (`/auth/verify`) instead of `/sessions`, so login succeeds when authentication is valid even if session listing is unavailable.

Also keep `/auth/status` anonymously accessible even when minimum CLI version enforcement is enabled, preserving auth discovery behavior.
