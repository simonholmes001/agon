---
"@agon_agents/cli": patch
---

Refine shell interaction and moderation behavior:

- replace dragged/pasted file paths inline with codex-style attachment tokens in the prompt input (`[File #n]` / `[Image #n]`) and color only the attachment segment
- update shell status guidance to focus on paste/drag attachment flow and include `/exit` / `Ctrl+C` exit hints
- improve direct-answer routing heuristics and tests so simple requests avoid unnecessary full agent-cycle orchestration
