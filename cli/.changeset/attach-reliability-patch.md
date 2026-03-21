---
"@agon_agents/cli": patch
---

Improve `/attach` reliability and diagnostics.

- Fix command parsing so trailing text cannot corrupt attachment file paths.
- Ensure attachment uploads use multipart form data (not forced JSON content type).
- Surface backend attachment error categories with clearer CLI guidance.
