---
"@agon_agents/cli": patch
---

Show `Ctrl+C to interrupt` hint on all running spinners.

- `shell`: running-state spinners (shimmer and debate-watch) now display `Ctrl+C to interrupt` so users have clear guidance on how to stop an in-progress operation.
- `start --watch`: progress spinner also shows the hint while monitoring debate progress.
- Extracted `buildInterruptHint()` in `renderer.ts` as the single source of truth for the hint text.
