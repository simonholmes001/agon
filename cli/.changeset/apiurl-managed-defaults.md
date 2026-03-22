---
"@agon_agents/cli": patch
---

Improve CLI config ergonomics around backend endpoint management.

- Add `apiUrl` ownership metadata (`default`/`user`/`admin`) and managed/custom mode reporting.
- Migrate legacy default HTTP endpoint usage to managed resolution so stale local config does not pin users to old hosts.
- Add `/unset <key>` and `agon config unset <key>` to remove overrides and return to managed defaults.
- Surface `apiUrl` source/mode and HTTPS-upgrade guidance in shell `/params`, header display, and `agon config` output.
