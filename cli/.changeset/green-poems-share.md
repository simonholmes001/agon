---
"@agon_agents/cli": patch
---

Align hosted endpoint resolution for HTTPS edge migration by adding `AGON_HOSTED_API_URL` and `AGON_API_HOSTNAME` fallbacks, while preserving `AGON_API_URL` as the highest-priority override.
