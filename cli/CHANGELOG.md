# @agon_agents/cli

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
