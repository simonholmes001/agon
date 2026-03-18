---
"@agon_agents/cli": minor
---

Shell UX: Ctrl+C now interrupts the current in-flight operation instead of exiting the session.

- Pressing Ctrl+C at the idle prompt clears the current input line and prints "Interrupted. Shell still active." — the shell remains open.
- Pressing Ctrl+C during active processing (spinner, follow-up polling, debate watch loop) cancels the in-flight operation and returns to the prompt without exiting.
- Normal `/exit`, `/quit`, and `/eot` behaviour is unchanged.
- Exports `INTERRUPT_SENTINEL`, `raceAbort`, and `isAbortError` utilities from `shell.ts`.
