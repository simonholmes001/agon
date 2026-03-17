---
"@agon_agents/cli": patch
---

Harden CLI runtime behavior by removing dead clarification API methods and adding live-watch failsafes (max duration, idle timeout, and retry failure stop) to avoid indefinite hangs.
