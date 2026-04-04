---
"@agon_agents/cli": patch
---

Security hardening: stop sending provider API keys over HTTP headers (server-managed keys only), harden local cache/artifact file permissions to 0o700 (dirs) and 0o600 (files).
