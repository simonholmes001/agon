---
"@agon_agents/cli": minor
---

Add first-time user authentication flow (`agon login`)

**New features:**
- New `agon login` command: guides users through saving a bearer token for authenticating against the Agon backend. Supports interactive entry, `--token` flag for non-interactive use, `--clear` to remove a stored token, and `--status` to check current authentication state.
- `AuthManager`: stores bearer tokens in `~/.agon/credentials` with mode `0600` (owner read/write only) — separate from `.agonrc` so the main config file can be version-controlled safely.
- `agon shell` now performs a pre-flight auth check on startup. When the backend requires authentication and no token is configured, the shell exits with a clear prompt directing users to run `agon login`.
- `agon start` picks up the stored credentials token automatically.
- HTTP 401/403 responses from the backend are now mapped to an `UNAUTHENTICATED` error code with actionable suggestions (run `agon login` or set `AGON_AUTH_TOKEN`).
- New `getAuthStatus()` API client method calls the backend's anonymous `/auth/status` endpoint to discover whether authentication is required before making any authenticated calls.

**Migration notes for existing users:**
- Users with `AGON_AUTH_TOKEN` or `AGON_BEARER_TOKEN` environment variables set are unaffected — the env vars continue to take precedence.
- Users connecting to backends with `Authentication:Enabled = false` (the default) see no change in behaviour.
- Users connecting to auth-enabled backends who previously relied on the token being silently absent will now see a clear error and be directed to run `agon login`.
