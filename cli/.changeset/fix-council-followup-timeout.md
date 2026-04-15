---
"@agon_agents/cli": patch
---

Prevent false failures for long-running `invoke council` follow-ups by increasing the message submission timeout and recovering from client-side timeouts by polling session/messages for the eventual response.
