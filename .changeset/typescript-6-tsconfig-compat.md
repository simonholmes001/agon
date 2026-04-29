---
"@agon_agents/cli": patch
---

Fix TypeScript 6.0 compatibility in tsconfig.json: add explicit `rootDir` (required by TS6 when `outDir` is set) and `types: ["node"]` (TS6 no longer auto-includes `@types/node`).
