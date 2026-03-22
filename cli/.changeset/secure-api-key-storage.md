---
"@agon_agents/cli": minor
---

Add secure per-user LLM provider API key storage and lifecycle management

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
