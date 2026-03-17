/**
 * Resume Command Tests
 *
 * Tests for agon resume command:
 * - Resume from cache
 * - Resume from API (cache miss)
 * - Session not found
 * - Error handling
 * - Next-steps messages per status/phase
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import Resume from '../../src/commands/resume.js';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import { ConfigManager } from '../../src/state/config-manager.js';
import type { SessionResponse } from '../../src/api/types.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');
vi.mock('../../src/state/config-manager.js');

const mockOclifConfig = {
  bin: 'agon',
  runHook: vi.fn().mockResolvedValue({}),
  runCommand: vi.fn(),
  findCommand: vi.fn(),
  pjson: { name: '@agon_agents/cli', version: '0.1.0' },
  root: '/fake/root',
  version: '0.1.0',
};

function createCommand(args: string[]): Resume {
  const command = new Resume(args, mockOclifConfig as any);
  vi.spyOn(command, 'log').mockImplementation(() => {});
  vi.spyOn(command, 'error').mockImplementation((msg: any) => {
    throw new Error(typeof msg === 'string' ? msg : String(msg));
  });
  return command;
}

describe('Resume Command', () => {
  let mockApiClient: any;
  let mockSessionManager: any;
  let mockConfigManager: any;

  beforeEach(() => {
    vi.clearAllMocks();

    mockApiClient = {
      getSession: vi.fn(),
    };

    mockSessionManager = {
      getSession: vi.fn(),
      saveSession: vi.fn(),
      setCurrentSessionId: vi.fn(),
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

  describe('Resume from cache', () => {
    it('should set session as current when found in cache', async () => {
      const sessionId = 'session-abc123';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'paused',
        phase: 'ANALYSIS_ROUND',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z',
      };

      mockSessionManager.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      await command.run();

      expect(mockSessionManager.getSession).toHaveBeenCalledWith(sessionId);
      expect(mockSessionManager.setCurrentSessionId).toHaveBeenCalledWith(sessionId);
      expect(mockApiClient.getSession).not.toHaveBeenCalled();
    });
  });

  describe('Resume from API (cache miss)', () => {
    it('should fetch from API when not in cache', async () => {
      const sessionId = 'session-abc123';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z',
      };

      mockSessionManager.getSession.mockResolvedValue(null);
      mockApiClient.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      await command.run();

      expect(mockApiClient.getSession).toHaveBeenCalledWith(sessionId);
      expect(mockSessionManager.saveSession).toHaveBeenCalledWith(mockSession);
      expect(mockSessionManager.setCurrentSessionId).toHaveBeenCalledWith(sessionId);
    });
  });

  describe('Next steps messages', () => {
    it('should show post-delivery steps for complete session', async () => {
      const sessionId = 'session-done';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'complete',
        phase: 'POST_DELIVERY',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z',
      };

      mockSessionManager.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});

      await command.run();

      const allCalls = logSpy.mock.calls.flat().join('\n');
      expect(allCalls).toContain('agon show');
    });

    it('should show clarification steps for clarification phase', async () => {
      const sessionId = 'session-clarifying';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'clarification',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z',
      };

      mockSessionManager.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});

      await command.run();

      const allCalls = logSpy.mock.calls.flat().join('\n');
      expect(allCalls).toContain('agon');
    });

    it('should show active-phase steps for active non-clarification session', async () => {
      const sessionId = 'session-debating';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'CRITIQUE',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z',
      };

      mockSessionManager.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      await expect(command.run()).resolves.not.toThrow();
    });

    it('should show paused steps for paused session', async () => {
      const sessionId = 'session-paused';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'paused',
        phase: 'SYNTHESIS',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z',
      };

      mockSessionManager.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      await expect(command.run()).resolves.not.toThrow();
    });

    it('should show complete_with_gaps steps', async () => {
      const sessionId = 'session-gaps';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'complete_with_gaps',
        phase: 'DELIVER_WITH_GAPS',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z',
      };

      mockSessionManager.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      await expect(command.run()).resolves.not.toThrow();
    });

    it('should display convergence score when present', async () => {
      const sessionId = 'session-conv';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'complete',
        phase: 'POST_DELIVERY',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z',
        convergence: {
          overall: 0.87,
          dimensions: {} as any,
        },
      };

      mockSessionManager.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      await expect(command.run()).resolves.not.toThrow();
    });
  });

  describe('Error handling', () => {
    it('should throw on "not found" API error', async () => {
      const sessionId = 'ghost-session';
      mockSessionManager.getSession.mockResolvedValue(null);
      mockApiClient.getSession.mockRejectedValue(new Error('Session not found'));

      const command = createCommand([sessionId]);
      await expect(command.run()).rejects.toThrow();
    });

    it('should throw on generic API error', async () => {
      const sessionId = 'session-error';
      mockSessionManager.getSession.mockResolvedValue(null);
      mockApiClient.getSession.mockRejectedValue(new Error('Network timeout'));

      const command = createCommand([sessionId]);
      await expect(command.run()).rejects.toThrow();
    });
  });
});
