---
"@agon_agents/cli": patch
---

Harden startup update checks and release metadata verification.

- Make shell startup update checks best-effort so transient update-check failures do not block shell startup.
- Add explicit startup update flow tests for skip-version, no-update, install success, and install failure paths.
- Add post-publish npm latest verification in release automation with robust path resolution and actionable failure reporting.
