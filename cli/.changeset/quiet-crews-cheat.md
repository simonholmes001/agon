---
"@agon_agents/cli": patch
---

Enable in-session CLI self-update with `/self-update` (alias `/update`) so updates can run without exiting shell first. Route `agon --self-update` through the `self-update` command path for consistent behavior and improve failure guidance for permission/network/file-lock scenarios.
