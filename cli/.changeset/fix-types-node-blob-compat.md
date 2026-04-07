---
"@agon_agents/cli": patch
---

Fix TypeScript compatibility issue with @types/node v25: convert Buffer to Uint8Array before passing to Blob constructor.
