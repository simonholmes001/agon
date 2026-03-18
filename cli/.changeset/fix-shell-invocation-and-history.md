---
"@agon_agents/cli": patch
---

Fix `node bin/run.js shell` error and add prompt history navigation.

- Remove `src/commands/index.ts` which caused oclif to treat the CLI as a single-command CLI (`SINGLE_COMMAND_CLI_SYMBOL`), making `node bin/run.js shell` fail with `Error: command Symbol(SINGLE_COMMAND_CLI):shell not found`.
- Add `clean` script to `build` so stale `dist/commands/index.js` is removed on every rebuild.
- Add Up/Down arrow prompt history navigation in the interactive shell: pressing `↑` on empty input loads the previous prompt; `↓` moves forward; at the newest entry `↓` returns to an empty input.
