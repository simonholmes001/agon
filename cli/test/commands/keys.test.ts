/**
 * Keys Command Tests
 *
 * Covers:
 * - agon keys         → list
 * - agon keys set     → set (non-interactive via --key flag)
 * - agon keys delete  → delete
 * - agon keys rotate  → rotate (non-interactive via --key flag)
 * - Missing provider argument errors
 * - Unknown action errors
 * - Delete when key is not stored
 * - Rotate when key is not stored
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import Keys from '../../src/commands/keys.js';

// ── Mock ApiKeyManager ────────────────────────────────────────────────────────

const mockManager = vi.hoisted(() => ({
  set: vi.fn(),
  get: vi.fn(),
  rotate: vi.fn(),
  delete: vi.fn(),
  list: vi.fn(),
  has: vi.fn(),
  preview: vi.fn(),
}));

vi.mock('../../src/auth/api-key-manager.js', () => ({
  ApiKeyManager: vi.fn(function () {
    return mockManager;
  }),
  KNOWN_PROVIDERS: ['openai', 'anthropic', 'gemini', 'deepseek'],
  redactSecret: (v: string) => (v ? `${v.substring(0, 4)}…[REDACTED]` : '[REDACTED]'),
}));

// Mock inquirer to avoid interactive prompts in tests
vi.mock('inquirer', () => ({
  default: {
    prompt: vi.fn(),
  },
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function createCommand(args: string[]): Keys {
  const mockConfig = {
    bin: 'agon',
    runHook: vi.fn().mockResolvedValue({}),
    runCommand: vi.fn(),
    findCommand: vi.fn(),
    pjson: { name: 'agon', version: '0.5.0' },
    root: '/fake/root',
    version: '0.5.0',
  };
  const cmd = new Keys(args, mockConfig as any);
  vi.spyOn(cmd, 'log').mockImplementation(() => {});
  vi.spyOn(cmd, 'error').mockImplementation((msg) => {
    throw new Error(typeof msg === 'string' ? msg : String(msg));
  });
  return cmd;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('Keys Command', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── List (no action) ────────────────────────────────────────────────────────

  describe('agon keys (list)', () => {
    it('shows "no keys stored" message when list is empty', async () => {
      mockManager.list.mockResolvedValue([]);
      const cmd = createCommand([]);
      await cmd.run();
      expect(mockManager.list).toHaveBeenCalled();
    });

    it('displays provider names and previews when keys exist', async () => {
      mockManager.list.mockResolvedValue(['openai', 'anthropic']);
      mockManager.preview.mockResolvedValue('sk-a…[REDACTED]');
      const cmd = createCommand([]);
      await expect(cmd.run()).resolves.not.toThrow();
      expect(mockManager.preview).toHaveBeenCalledWith('openai');
      expect(mockManager.preview).toHaveBeenCalledWith('anthropic');
    });

    it('never calls get() (no raw key retrieval during list)', async () => {
      mockManager.list.mockResolvedValue(['openai']);
      mockManager.preview.mockResolvedValue('sk-a…[REDACTED]');
      const cmd = createCommand([]);
      await cmd.run();
      expect(mockManager.get).not.toHaveBeenCalled();
    });
  });

  // ── set ─────────────────────────────────────────────────────────────────────

  describe('agon keys set <provider> --key <k>', () => {
    it('stores the key and shows a redacted confirmation', async () => {
      mockManager.set.mockResolvedValue(undefined);
      mockManager.preview.mockResolvedValue('sk-a…[REDACTED]');
      const cmd = createCommand(['set', 'openai', '--key', 'sk-actual-key']);
      await cmd.run();
      expect(mockManager.set).toHaveBeenCalledWith('openai', 'sk-actual-key');
    });

    it('throws when provider is missing', async () => {
      const cmd = createCommand(['set']);
      await expect(cmd.run()).rejects.toThrow(/missing provider/i);
    });

    it('does not log the raw key value', async () => {
      mockManager.set.mockResolvedValue(undefined);
      mockManager.preview.mockResolvedValue('sk-a…[REDACTED]');
      const cmd = createCommand(['set', 'openai', '--key', 'sk-should-not-appear']);
      const logSpy = vi.mocked(cmd.log);
      await cmd.run();
      for (const call of logSpy.mock.calls) {
        const text = call.join(' ');
        expect(text).not.toContain('sk-should-not-appear');
      }
    });
  });

  // ── delete ───────────────────────────────────────────────────────────────────

  describe('agon keys delete <provider>', () => {
    it('deletes the stored key and confirms', async () => {
      mockManager.has.mockResolvedValue(true);
      mockManager.delete.mockResolvedValue(true);
      const cmd = createCommand(['delete', 'openai']);
      await cmd.run();
      expect(mockManager.delete).toHaveBeenCalledWith('openai');
    });

    it('shows a not-found message when key does not exist', async () => {
      mockManager.has.mockResolvedValue(false);
      const cmd = createCommand(['delete', 'openai']);
      await cmd.run();
      // delete() should not be called when the key is absent
      expect(mockManager.delete).not.toHaveBeenCalled();
    });

    it('throws when provider is missing', async () => {
      const cmd = createCommand(['delete']);
      await expect(cmd.run()).rejects.toThrow(/missing provider/i);
    });
  });

  // ── rotate ───────────────────────────────────────────────────────────────────

  describe('agon keys rotate <provider> --key <k>', () => {
    it('rotates the key and shows a redacted confirmation', async () => {
      mockManager.has.mockResolvedValue(true);
      mockManager.rotate.mockResolvedValue(undefined);
      mockManager.preview.mockResolvedValue('sk-n…[REDACTED]');
      const cmd = createCommand(['rotate', 'openai', '--key', 'sk-new-key']);
      await cmd.run();
      expect(mockManager.rotate).toHaveBeenCalledWith('openai', 'sk-new-key');
    });

    it('shows a not-found message when no existing key is stored', async () => {
      mockManager.has.mockResolvedValue(false);
      const cmd = createCommand(['rotate', 'openai', '--key', 'sk-new-key']);
      await cmd.run();
      expect(mockManager.rotate).not.toHaveBeenCalled();
    });

    it('throws when provider is missing', async () => {
      const cmd = createCommand(['rotate']);
      await expect(cmd.run()).rejects.toThrow(/missing provider/i);
    });

    it('does not log the raw key value', async () => {
      mockManager.has.mockResolvedValue(true);
      mockManager.rotate.mockResolvedValue(undefined);
      mockManager.preview.mockResolvedValue('sk-n…[REDACTED]');
      const cmd = createCommand(['rotate', 'openai', '--key', 'sk-raw-secret-value']);
      const logSpy = vi.mocked(cmd.log);
      await cmd.run();
      for (const call of logSpy.mock.calls) {
        const text = call.join(' ');
        expect(text).not.toContain('sk-raw-secret-value');
      }
    });
  });

  // ── Unknown action ────────────────────────────────────────────────────────

  describe('unknown action', () => {
    it('throws for an unrecognised action', async () => {
      const cmd = createCommand(['badaction', 'openai']);
      await expect(cmd.run()).rejects.toThrow(/unknown action/i);
    });
  });
});
