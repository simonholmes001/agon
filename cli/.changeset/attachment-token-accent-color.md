---
"@agon_agents/cli": minor
---

Style attached file references with a Codex-like accent color

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
