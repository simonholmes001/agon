# @agon_agents/cli

## 0.6.4

### Patch Changes

- Enforce token-first startup for backend-interacting CLI commands:

  - `agon shell` and `agon start` now require a configured bearer token by default
  - when no token exists, CLI exits with explicit setup guidance (`agon login`, `agon login --status`)
  - local development bypass remains available with `AGON_ALLOW_ANONYMOUS=true`

- Improve command discoverability by surfacing top-level launcher commands directly in help surfaces:

  - add a full command catalog to `agon --help`
  - show the same top-level launcher command list in in-shell `/help`

## 0.6.3

### Patch Changes

- chore(deps): bump ora from 8.2.0 to 9.3.0

## 0.6.2

### Patch Changes

- Improve attachment experience and output rendering:

  - stop auto-highlighting plain slash-separated prose (for example CV/resume, CI/CD)
  - add follow-up scoping hint after implicit attachment so responses focus on the newly attached file/image
  - avoid submitting prompts when an implicit drag/paste file path cannot be resolved locally
  - improve image extraction fallback behavior by ignoring refusal-like extractor outputs

- Harden shell reliability for attachment follow-ups and reduce confusing guidance:

  - recover automatically when a stale/deleted session ID causes a follow-up `SESSION_NOT_FOUND` error
  - clear stale local session pointers before retrying as a new idea
  - update shell next-step hints to prefer paste/drag file-path flow instead of `/attach` guidance

## 0.6.1

### Patch Changes

- Improve attachment UX and routing behavior:

  - show codex-style attachment references in shell output (`[Image #n]`, `[File #n]`) with distinct image/file coloring
  - improve attachment feedback readability by separating reference label and filename
  - keep simple image-description prompts in direct-answer mode instead of triggering full debate flow

- Improve reliability for simple-query routing and image attachment handling:

  - prevent accidental message submission when implicit drag/paste attachment path cannot be resolved
  - expand image MIME/extension recognition for upload and extraction paths
  - improve extraction compatibility for additional OpenAI vision response shapes
  - keep simple requests (including image-description asks) on deterministic direct-answer path to avoid unnecessary full debate cycles

- Improve shell UX and reliability in three areas:

  - Make `/update` restart guidance explicit so users know they must exit and relaunch to run the newly installed runtime.
  - Improve attachment feedback for image uploads when backend extraction returns no vision text.
  - Keep "attach anytime" flow behavior while clarifying extraction outcomes in shell output.

- Refine shell interaction and moderation behavior:

  - replace dragged/pasted file paths inline with codex-style attachment tokens in the prompt input (`[File #n]` / `[Image #n]`) and color only the attachment segment
  - update shell status guidance to focus on paste/drag attachment flow and include `/exit` / `Ctrl+C` exit hints
  - improve direct-answer routing heuristics and tests so simple requests avoid unnecessary full agent-cycle orchestration

## 0.6.0

### Minor Changes

- Add secure per-user LLM provider API key storage and lifecycle management

  **New features:**

  - `SecretStore`: AES-256-GCM encrypted file-based secret store. Generates a random 256-bit encryption key on first use, stored at `~/.agon/keystore.key` (mode `0400`, owner read-only). Encrypted secrets stored at `~/.agon/api-keys` (mode `0600`, owner read/write). Each entry uses an independent random IV. Exports `redactSecret()` utility that safely redacts secret values for user-facing output.
  - `ApiKeyManager`: lifecycle manager for named LLM provider API keys (`openai`, `anthropic`, `gemini`, `deepseek`, and custom providers). Wraps `SecretStore` with `set`, `get`, `rotate`, `delete`, `list`, `has`, and `preview` operations. Key material never appears in error messages or log output. `rotate()` is a single atomic write with no gap where the key is absent.
  - `agon keys` command: manage stored API keys from the CLI.
    - `agon keys` — list stored providers with a redacted key preview
    - `agon keys set <provider>` — store a key (interactive masked prompt or `--key` flag)
    - `agon keys rotate <provider>` — atomically replace a stored key
    - `agon keys delete <provider>` — remove a stored key

  **Security properties:**

  - Secrets are encrypted at rest; key file is owner-read-only (`0400`)
  - Decryption failures return `null` silently — no internal state is leaked
  - Key material is absent from all error messages, log calls, and `list()` output
  - Provider names are whitelist-validated (alphanumeric, hyphens, and underscores only)

## 0.5.1

### Patch Changes

- Improve shell attachment UX by allowing file-path-first input without requiring `/attach`.

  - Auto-attach when plain input starts with a valid local file path.
  - Support escaped-space file paths commonly produced by terminal drag-and-drop.
  - If path input includes trailing text, attach first then treat trailing text as the follow-up message.
  - Keep explicit `/attach` behavior unchanged.

## 0.5.0

### Minor Changes

- Add first-time user authentication flow (`agon login`)

  **New features:**

  - New `agon login` command: guides users through saving a bearer token for authenticating against the Agon backend. Supports interactive entry, `--token` flag for non-interactive use, `--clear` to remove a stored token, and `--status` to check current authentication state.
  - `AuthManager`: stores bearer tokens in `~/.agon/credentials` with mode `0600` (owner read/write only) — separate from `.agonrc` so the main config file can be version-controlled safely.
  - `agon shell` now performs a pre-flight auth check on startup. When the backend requires authentication and no token is configured, the shell exits with a clear prompt directing users to run `agon login`.
  - `agon start` picks up the stored credentials token automatically.
  - HTTP 401/403 responses from the backend are now mapped to an `UNAUTHENTICATED` error code with actionable suggestions (run `agon login` or set `AGON_AUTH_TOKEN`).
  - New `getAuthStatus()` API client method calls the backend's anonymous `/auth/status` endpoint to discover whether authentication is required before making any authenticated calls.

  **Migration notes for existing users:**

  - Users with `AGON_AUTH_TOKEN` or `AGON_BEARER_TOKEN` environment variables set are unaffected — the env vars continue to take precedence.
  - Users connecting to backends with `Authentication:Enabled = false` (the default) see no change in behaviour.
  - Users connecting to auth-enabled backends who previously relied on the token being silently absent will now see a clear error and be directed to run `agon login`.

### Patch Changes

- Improve CLI config ergonomics around backend endpoint management.

  - Add `apiUrl` ownership metadata (`default`/`user`/`admin`) and managed/custom mode reporting.
  - Migrate legacy default HTTP endpoint usage to managed resolution so stale local config does not pin users to old hosts.
  - Add `/unset <key>` and `agon config unset <key>` to remove overrides and return to managed defaults.
  - Surface `apiUrl` source/mode and HTTPS-upgrade guidance in shell `/params`, header display, and `agon config` output.

- Fix CLI CI test discovery by preserving Vitest default exclusions while still excluding `.worktrees/**` mirrors.

## 0.4.3

### Patch Changes

- Enable true "attach anytime" behavior by auto-creating a session when none is active, and by recovering from stale session pointers when upload returns SESSION_NOT_FOUND.

## 0.4.2

### Patch Changes

- Align hosted endpoint resolution for HTTPS edge migration by adding `AGON_HOSTED_API_URL` and `AGON_API_HOSTNAME` fallbacks, while preserving `AGON_API_URL` as the highest-priority override.

## 0.4.1

### Patch Changes

- Improve `/attach` reliability and diagnostics.

  - Fix command parsing so trailing text cannot corrupt attachment file paths.
  - Ensure attachment uploads use multipart form data (not forced JSON content type).
  - Surface backend attachment error categories with clearer CLI guidance.

## 0.4.0

### Minor Changes

- Style attached file references with a Codex-like accent color

  Introduces two new exports from `shell/renderer`:

  - `styleAttachmentToken(token)` — applies the canonical amber accent color
    (`chalk.bold.rgb(248, 197, 71)`) to a single file-name or Codex-style token.
  - `highlightAttachmentRefs(text)` — scans rendered text for file paths
    (`./path`, `../path`, `/absolute/path`) and Codex-style bracketed tokens
    (`[Image …]`, `[File …]`, `[Attachment …]`) and styles each match with the
    same accent color.

  Surface-level changes:

  - `renderMessagePanel` now post-processes rendered Markdown through
    `highlightAttachmentRefs`, so attachment references in assistant/moderator
    messages are visually highlighted.
  - The `attachment` outcome in the shell command now renders the file name via
    `styleAttachmentToken`, making the attached-file confirmation immediately
    scannable.
  - Inline-attach confirmation prints in the shell engine also use
    `styleAttachmentToken` for the file name.

  Non-attachment text color behavior is unchanged.

## 0.3.2

### Patch Changes

- Show `Ctrl+C to interrupt` hint on all running spinners.

  - `shell`: running-state spinners (shimmer and debate-watch) now display `Ctrl+C to interrupt` so users have clear guidance on how to stop an in-progress operation.
  - `start --watch`: progress spinner also shows the hint while monitoring debate progress.
  - Extracted `buildInterruptHint()` in `renderer.ts` as the single source of truth for the hint text.

## 0.3.1

### Patch Changes

- Print a resume hint on shell exit and improve the `resume` command.

  - On `/exit`, `/quit`, `/eot`: print `To continue this session, run: agon resume <session-id>` when a session was active
  - `resume` command: improve "not found" error to suggest `agon sessions` as recovery
  - `resume` command: add a concrete UUID example and `agon sessions` pointer to help text

## 0.3.0

### Minor Changes

- Shell: Ctrl+C now exits Agon when the input zone is empty.

  Previously, pressing Ctrl+C at an empty prompt printed "Interrupted. Shell still active." and kept the shell running. Now:

  - **Empty input zone**: Ctrl+C exits the shell (prints "Exiting shell.").
  - **Non-empty input zone**: Ctrl+C interrupts and stays in the shell (existing behavior unchanged).

## 0.2.1

### Patch Changes

- Add bracketed elapsed timer indicator during agent reasoning

  While the agent spinner is active, a live `[Xs]` timer now appears inline
  with the thinking status text (e.g. `Agents are analyzing... (Analysis Round) [7s]`),
  mirroring Codex behaviour. The timer resets to `[0s]` at the start of each new
  thinking cycle (phase transition or spinner restart after output).

## 0.2.0

### Minor Changes

- Shell UX: Ctrl+C now interrupts the current in-flight operation instead of exiting the session.

  - Pressing Ctrl+C at the idle prompt clears the current input line and prints "Interrupted. Shell still active." — the shell remains open.
  - Pressing Ctrl+C during active processing (spinner, follow-up polling, debate watch loop) cancels the in-flight operation and returns to the prompt without exiting.
  - Normal `/exit`, `/quit`, and `/eot` behaviour is unchanged.
  - Exports `INTERRUPT_SENTINEL`, `raceAbort`, and `isAbortError` utilities from `shell.ts`.

### Patch Changes

- Fix `node bin/run.js shell` error and add prompt history navigation.

  - Remove `src/commands/index.ts` which caused oclif to treat the CLI as a single-command CLI (`SINGLE_COMMAND_CLI_SYMBOL`), making `node bin/run.js shell` fail with `Error: command Symbol(SINGLE_COMMAND_CLI):shell not found`.
  - Add `clean` script to `build` so stale `dist/commands/index.js` is removed on every rebuild.
  - Add Up/Down arrow prompt history navigation in the interactive shell: pressing `↑` on empty input loads the previous prompt; `↓` moves forward; at the newest entry `↓` returns to an empty input.

- Fix shell UI: vertically center `>` prompt in the idle input box.

  Reduced `inputLineCount` from 4 to 3 in `createPromptFrame()` so the prompt
  row sits at equal distance from the top and bottom borders of the input zone
  (1 blank line above, 1 blank line below), matching the intended Codex-style
  layout. Multiline wrap and cursor-position behaviour are unaffected.

- Rename in-shell update command from `/self-update` to `/update`. The `/update [--check]` command is now the sole in-session update entry point. The startup banner and `/help` listing have been updated accordingly.

- Add hosted endpoint resolution precedence for HTTPS migration:

  - Keep `AGON_API_URL` as the highest-priority runtime override.
  - Add `AGON_HOSTED_API_URL` (full URL) and `AGON_API_HOSTNAME` (`https://<hostname>`) as hosted defaults.
  - Preserve legacy fallback behavior for environments not yet migrated to hostname-based HTTPS.

## 0.1.14

### Patch Changes

- Update the CLI hosted default API endpoint to the current dev Application Gateway public ingress (`http://4.225.205.12`) while preserving `AGON_API_URL` override behavior for local testing.

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
