# @agon_agents/cli

## 0.1.13

### Patch Changes

- Remediate all 32 npm dependency vulnerabilities (27 high, 5 moderate).

  - Upgrade `@typescript-eslint/eslint-plugin` and `@typescript-eslint/parser` from `^6.13.0` to `^8.0.0` to fix three minimatch ReDoS advisories (GHSA-3ppc-4f35-3m26, GHSA-7r86-cg39-jmmj, GHSA-23c5-xmqv-rm74).
  - Upgrade `vitest` and `@vitest/coverage-v8` from `^1.0.0` to `^4.1.0` to fix the esbuild dev-server CORS bypass (GHSA-67mh-4wv8-2f99).
  - Add `overrides` for `fast-xml-parser: "^5.5.6"` to fix the numeric entity expansion bypass (GHSA-8gc5-j5rx-235r) in the `oclif` → `@aws-sdk/xml-builder` transitive dependency chain.

- Fix shell `/attach` handling so inline attach commands in chat input are executed before message routing, and improve attachment file errors with actionable guidance.

## 0.1.12

### Patch Changes

- Enable in-session CLI self-update with `/self-update` (alias `/update`) so updates can run without exiting shell first. Route `agon --self-update` through the `self-update` command path for consistent behavior and improve failure guidance for permission/network/file-lock scenarios.

## 0.1.11

### Patch Changes

- Sort `/help` command list alphabetically for predictable lookup

## 0.1.10

### Patch Changes

- Add npm provenance attestation support via .npmrc (provenance=true) to cryptographically link every published release to its source commit and GitHub Actions build.

## 0.1.9

### Patch Changes

- Harden CLI runtime behavior by removing dead clarification API methods and adding live-watch failsafes (max duration, idle timeout, and retry failure stop) to avoid indefinite hangs.
- Add shell `/attach <file-path>` support for session documents, update slash-command help/next-step guidance, and improve shell/API consistency for refresh and follow-up flows.

## 0.1.8

### Patch Changes

- Improve follow-up reliability and UX by mapping gateway timeouts (HTTP 504) to backend-unavailable guidance, preserving moderator question numbering in shell rendering, and refining moderator prompt behavior to ask adaptive non-repetitive clarifications.

## 0.1.7

### Patch Changes

- Clarify shell update instructions so users are explicitly told to exit the interactive shell before running update commands in the terminal.
- Improve top-level CLI help so `agon --help` explicitly lists global flags including `--help`, `--version`, and `--self-update`.

## 0.1.6

### Patch Changes

- Add consistent self-update entrypoint support so `agon --self-update` works directly from the launcher and keep `agon self-update` compatible.

## 0.1.5

### Patch Changes

- Refine the shell input box to use a Codex-style compact landing layout with wider prompt width, unlimited typing length, and scrollable multiline cursor visibility.
- Improve shell prompt input wrapping by preferring word-boundary line breaks and increasing editable multiline input space.

## 0.1.4

### Patch Changes

- Add `agon self-update`, send CLI version headers on API requests, and show clearer upgrade guidance when backend requires a newer CLI version.
- Fix shell prompt rendering so the `>` marker stays vertically aligned when typing starts.

## 0.1.3

### Patch Changes

- Default the CLI API endpoint to the hosted gateway for zero-config installs, while preserving local testing via the `AGON_API_URL` environment variable.
- Center the shell prompt anchor (`>`) vertically within the input box to match the Codex-style prompt layout.

## 0.1.2

### Patch Changes

- Improve shell input UX by wrapping prompt text within the input box and adding cursor-based editing/navigation, including arrow movement, word jumps, and in-line insertion/deletion.

## 0.1.1

### Patch Changes

- Add startup npm update notifications in shell mode and support `agon --version` directly from the launcher.
