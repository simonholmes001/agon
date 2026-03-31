---
"@agon_agents/cli": patch
---

Prevent URL-like follow-up text (including split `https:\n//...` forms) from being misclassified as implicit local file attachments in shell input parsing.
