---
"@agon_agents/cli": patch
---

Fix shell UI: vertically center `>` prompt in the idle input box.

Reduced `inputLineCount` from 4 to 3 in `createPromptFrame()` so the prompt
row sits at equal distance from the top and bottom borders of the input zone
(1 blank line above, 1 blank line below), matching the intended Codex-style
layout. Multiline wrap and cursor-position behaviour are unaffected.
