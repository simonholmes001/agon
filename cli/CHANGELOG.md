# @agon_agents/cli

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
