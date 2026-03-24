---
'@agon_agents/cli': patch
---

Add native Entra device-code login to `agon login` and make it the default auth path when backend metadata is available, while retaining Azure CLI and manual token fallbacks.
