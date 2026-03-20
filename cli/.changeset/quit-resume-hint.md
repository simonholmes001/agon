---
"@agon_agents/cli": patch
---

Print a resume hint on shell exit and improve the `resume` command.

- On `/exit`, `/quit`, `/eot`: print `To continue this session, run: agon resume <session-id>` when a session was active
- `resume` command: improve "not found" error to suggest `agon sessions` as recovery
- `resume` command: add a concrete UUID example and `agon sessions` pointer to help text
