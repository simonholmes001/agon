---
"@agon_agents/cli": patch
---

Improve attachment experience and output rendering:

- stop auto-highlighting plain slash-separated prose (for example CV/resume, CI/CD)
- add follow-up scoping hint after implicit attachment so responses focus on the newly attached file/image
- avoid submitting prompts when an implicit drag/paste file path cannot be resolved locally
- improve image extraction fallback behavior by ignoring refusal-like extractor outputs
