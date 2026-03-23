---
"@agon_agents/cli": patch
---

Harden shell reliability for attachment follow-ups and reduce confusing guidance:

- recover automatically when a stale/deleted session ID causes a follow-up `SESSION_NOT_FOUND` error
- clear stale local session pointers before retrying as a new idea
- update shell next-step hints to prefer paste/drag file-path flow instead of `/attach` guidance
