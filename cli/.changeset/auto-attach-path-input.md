---
"@agon_agents/cli": patch
---

Improve shell attachment UX by allowing file-path-first input without requiring `/attach`.

- Auto-attach when plain input starts with a valid local file path.
- Support escaped-space file paths commonly produced by terminal drag-and-drop.
- If path input includes trailing text, attach first then treat trailing text as the follow-up message.
- Keep explicit `/attach` behavior unchanged.
