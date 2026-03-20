---
"@agon_agents/cli": minor
---

Shell: Ctrl+C now exits Agon when the input zone is empty.

Previously, pressing Ctrl+C at an empty prompt printed "Interrupted. Shell still active." and kept the shell running. Now:

- **Empty input zone**: Ctrl+C exits the shell (prints "Exiting shell.").
- **Non-empty input zone**: Ctrl+C interrupts and stays in the shell (existing behavior unchanged).
