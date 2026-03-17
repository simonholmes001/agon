/**
 * Show Command Tests
 *
 * Tests for agon show command:
 * - Display artifact from cache
 * - Fetch artifact from API (cache miss)
 * - Handle --refresh flag
 * - Handle --raw flag
 * - Handle --session flag
 * - Error handling (artifact not found, no session)
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import Show from '../../src/commands/show.js';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import { ConfigManager } from '../../src/state/config-manager.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');
vi.mock('../../src/state/config-manager.js');
vi.mock('../../src/utils/markdown.js', () => ({
  renderMarkdown: vi.fn((content: string) => content),
}));
vi.mock('../../src/utils/logger.js', () => ({
  Logger: vi.fn(() => ({
    debug: vi.fn(),
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
  })),
}));

const mockOclifConfig = {
  bin: 'agon',
  runHook: vi.fn().mockResolvedValue({}),
  runCommand: vi.fn(),
  findCommand: vi.fn(),
  pjson: { name: '@agon_agents/cli', version: '0.1.0' },
  root: '/fake/root',
  version: '0.1.0',
};

function createCommand(args: string[]): Show {
  const command = new Show(args, mockOclifConfig as any);
  vi.spyOn(command, 'log').mockImplementation(() => {});
  vi.spyOn(command, 'error').mockImplementation((msg: any) => {
    throw new Error(typeof msg === 'string' ? msg : String(msg));
  });
  return command;
}

describe('Show Command', () => {
  let mockApiClient: any;
  let mockSessionManager: any;
  let mockConfigManager: any;

  beforeEach(() => {
    vi.clearAllMocks();

    mockApiClient = {
      getArtifact: vi.fn(),
    };

    mockSessionManager = {
      getCurrentSessionId: vi.fn(),
      getArtifact: vi.fn(),
      saveArtifact: vi.fn(),
    };

    mockConfigManager = {
      load: vi.fn().mockResolvedValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info',
      }),
    };

    vi.mocked(AgonAPIClient).mockImplementation(() => mockApiClient);
    vi.mocked(SessionManager).mockImplementation(() => mockSessionManager);
    vi.mocked(ConfigManager).mockImplementation(() => mockConfigManager);
  });

  describe('No active session', () => {
    it('should print message and return when no session is set and no --session flag', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand(['verdict']);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('No active session found');
    });
  });

  describe('Serving from cache', () => {
    it('should display cached artifact without hitting API', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      mockSessionManager.getArtifact.mockResolvedValue('# Verdict\nContent here');

      const command = createCommand(['verdict']);
      await command.run();

      expect(mockSessionManager.getArtifact).toHaveBeenCalledWith('session-abc', 'verdict');
      expect(mockApiClient.getArtifact).not.toHaveBeenCalled();
    });

    it('should hit API when cache is empty', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      mockSessionManager.getArtifact.mockResolvedValue(null);
      mockApiClient.getArtifact.mockResolvedValue({ content: '# Plan\nContent', type: 'plan' });

      const command = createCommand(['plan']);
      await command.run();

      expect(mockApiClient.getArtifact).toHaveBeenCalledWith('session-abc', 'plan');
      expect(mockSessionManager.saveArtifact).toHaveBeenCalledWith('session-abc', 'plan', '# Plan\nContent');
    });
  });

  describe('--refresh flag', () => {
    it('should bypass cache and fetch from API when --refresh is set', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      mockApiClient.getArtifact.mockResolvedValue({ content: '# Fresh Verdict', type: 'verdict' });

      const command = createCommand(['verdict', '--refresh']);
      await command.run();

      expect(mockSessionManager.getArtifact).not.toHaveBeenCalled();
      expect(mockApiClient.getArtifact).toHaveBeenCalledWith('session-abc', 'verdict');
    });
  });

  describe('--session flag', () => {
    it('should use the session ID provided via --session flag', async () => {
      mockSessionManager.getArtifact.mockResolvedValue('# PRD');

      const command = createCommand(['prd', '--session', 'explicit-session-id']);
      await command.run();

      expect(mockSessionManager.getArtifact).toHaveBeenCalledWith('explicit-session-id', 'prd');
    });
  });

  describe('--raw flag', () => {
    it('should display raw Markdown without rendering when --raw is set', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const rawContent = '# Raw PRD\n\nContent';
      mockSessionManager.getArtifact.mockResolvedValue(rawContent);

      const command = createCommand(['prd', '--raw']);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain(rawContent);
    });

    it('should render Markdown when --raw is not set', async () => {
      const { renderMarkdown } = await import('../../src/utils/markdown.js');
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const content = '# Verdict\n\nContent';
      mockSessionManager.getArtifact.mockResolvedValue(content);

      const command = createCommand(['verdict']);
      await command.run();

      expect(renderMarkdown).toHaveBeenCalledWith(content);
    });
  });

  describe('Artifact not found', () => {
    it('should print not-found message when API returns not-found error', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      mockSessionManager.getArtifact.mockResolvedValue(null);
      mockApiClient.getArtifact.mockRejectedValue(new Error('Artifact not found'));

      const command = createCommand(['verdict', '--refresh']);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('not found');
    });

    it('should rethrow unexpected API errors', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      mockSessionManager.getArtifact.mockResolvedValue(null);
      mockApiClient.getArtifact.mockRejectedValue(new Error('Internal server error'));

      const command = createCommand(['verdict', '--refresh']);
      await expect(command.run()).rejects.toThrow();
    });
  });

  describe('All artifact types', () => {
    const types = ['verdict', 'plan', 'prd', 'risks', 'assumptions', 'architecture', 'copilot'] as const;

    for (const type of types) {
      it(`should display ${type} artifact`, async () => {
        mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
        mockSessionManager.getArtifact.mockResolvedValue(`# ${type}\nContent`);

        const command = createCommand([type]);
        await expect(command.run()).resolves.not.toThrow();
      });
    }
  });

  describe('Empty content', () => {
    it('should handle empty artifact content gracefully', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      mockSessionManager.getArtifact.mockResolvedValue('   ');

      const command = createCommand(['verdict']);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('No content available');
    });
  });
});
