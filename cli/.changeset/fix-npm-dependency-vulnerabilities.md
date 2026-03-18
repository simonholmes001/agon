---
"@agon_agents/cli": patch
---

Remediate all 32 npm dependency vulnerabilities (27 high, 5 moderate).

- Upgrade `@typescript-eslint/eslint-plugin` and `@typescript-eslint/parser` from `^6.13.0` to `^8.0.0` to fix three minimatch ReDoS advisories (GHSA-3ppc-4f35-3m26, GHSA-7r86-cg39-jmmj, GHSA-23c5-xmqv-rm74).
- Upgrade `vitest` and `@vitest/coverage-v8` from `^1.0.0` to `^4.1.0` to fix the esbuild dev-server CORS bypass (GHSA-67mh-4wv8-2f99).
- Add `overrides` for `fast-xml-parser: "^5.5.6"` to fix the numeric entity expansion bypass (GHSA-8gc5-j5rx-235r) in the `oclif` → `@aws-sdk/xml-builder` transitive dependency chain.
