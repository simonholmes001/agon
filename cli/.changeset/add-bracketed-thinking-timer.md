---
"@agon_agents/cli": patch
---

Add bracketed elapsed timer indicator during agent reasoning

While the agent spinner is active, a live `[Xs]` timer now appears inline
with the thinking status text (e.g. `Agents are analyzing... (Analysis Round) [7s]`),
mirroring Codex behaviour. The timer resets to `[0s]` at the start of each new
thinking cycle (phase transition or spinner restart after output).
