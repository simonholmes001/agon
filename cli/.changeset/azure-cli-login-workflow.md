---
"@agon_agents/cli": minor
---

Add first-class Azure CLI login workflow for bearer token acquisition.

Highlights:
- Add `agon login --azure-cli --scope <scope> [--tenant <tenant-id>]`.
- Add interactive method selection in `agon login` (Azure CLI recommended vs manual token paste).
- Normalize Entra scope inputs (`<app-id>` / `api://<app-id>` / `api://<app-id>/.default`).
- Add clearer auth guidance in `shell` and `start` first-time setup output.
- Document `AGON_AUTH_SCOPE` as a prefill convenience for interactive login.
