---
"@agon_agents/cli": patch
---

Improve reliability for simple-query routing and image attachment handling:

- prevent accidental message submission when implicit drag/paste attachment path cannot be resolved
- expand image MIME/extension recognition for upload and extraction paths
- improve extraction compatibility for additional OpenAI vision response shapes
- keep simple requests (including image-description asks) on deterministic direct-answer path to avoid unnecessary full debate cycles
