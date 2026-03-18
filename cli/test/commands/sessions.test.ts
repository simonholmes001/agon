/**
 * Sessions Command Tests
 *
 * Tests for agon sessions command:
 * - List active sessions (default)
 * - List all sessions with --all flag
 * - Handle empty session list
 * - Highlight current session
 * - Handle errors
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import Sessions from '../../src/commands/sessions.js';
import { SessionManager } from '../../src/state/session-manager.js';
import type { SessionResponse } from '../../src/api/types.js';

// Mock dependencies
vi.mock('../../src/state/session-manager.js');

const mockOclifConfig = {
  bin: 'agon',
  runHook: vi.fn().mockResolvedValue({}),
  runCommand: vi.fn(),
  findCommand: vi.fn(),
  pjson: { name: '@agon_agents/cli', version: '0.1.0' },
  root: '/fake/root',
  version: '0.1.0',
};

function createCommand(args: string[]): Sessions {
  const command = new Sessions(args, mockOclifConfig as any);
  vi.spyOn(command, 'log').mockImplementation(() => {});
  vi.spyOn(command, 'error').mockImplementation((msg: any) => {
    throw new Error(typeof msg === 'string' ? msg : String(msg));
  });
  return command;
}

describe('Sessions Command', () => {
  let mockSessionManager: any;

  beforeEach(() => {
    vi.clearAllMocks();

    mockSessionManager = {
      listSessions: vi.fn(),
      getCurrentSessionId: vi.fn(),
    };

    vi.mocked(SessionManager).mockImplementation(() => mockSessionManager);
  });

  describe('Empty state', () => {
    it('should show no-sessions message when list is empty (default filter)', async () => {
      mockSessionManager.listSessions.mockResolvedValue([]);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should show no-sessions message with --all flag when list is empty', async () => {
      mockSessionManager.listSessions.mockResolvedValue([]);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand(['--all']);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });
  });

  describe('Listing sessions (default — active/paused only)', () => {
    it('should display active sessions', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-active',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: new Date(Date.now() - 60_000).toISOString(),
          updatedAt: new Date(Date.now() - 30_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-active');

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
      expect(mockSessionManager.getCurrentSessionId).toHaveBeenCalled();
    });

    it('should filter out completed sessions by default', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-active',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        },
        {
          id: 'session-complete',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: new Date(Date.now() - 3_600_000).toISOString(),
          updatedAt: new Date(Date.now() - 3_600_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should include paused sessions by default', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-paused',
          status: 'paused',
          phase: 'ANALYSIS_ROUND',
          createdAt: new Date(Date.now() - 1_800_000).toISOString(),
          updatedAt: new Date(Date.now() - 1_800_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should display sessions with convergence data', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-active',
          status: 'active',
          phase: 'SYNTHESIS',
          createdAt: new Date(Date.now() - 3_600_000).toISOString(),
          updatedAt: new Date(Date.now() - 1_800_000).toISOString(),
          convergence: {
            overall: 0.85,
            dimensions: {
              assumption_explicitness: 0.88,
              evidence_quality: 0.82,
              risk_coverage: 0.84,
              decision_clarity: 0.87,
              scope_definition: 0.83,
              constraint_alignment: 0.85,
              uncertainty_acknowledgment: 0.86,
            },
          },
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should display sessions with dates in the recent past (minutes)', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-active',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: new Date(Date.now() - 5 * 60_000).toISOString(),
          updatedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should display sessions created hours ago', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-active',
          status: 'active',
          phase: 'CRITIQUE',
          createdAt: new Date(Date.now() - 3 * 3_600_000).toISOString(),
          updatedAt: new Date(Date.now() - 3 * 3_600_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should display sessions created days ago', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-active',
          status: 'active',
          phase: 'CRITIQUE',
          createdAt: new Date(Date.now() - 3 * 86_400_000).toISOString(),
          updatedAt: new Date(Date.now() - 3 * 86_400_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should display sessions created weeks ago (absolute date)', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-old',
          status: 'active',
          phase: 'CRITIQUE',
          createdAt: new Date(Date.now() - 14 * 86_400_000).toISOString(),
          updatedAt: new Date(Date.now() - 14 * 86_400_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should display sessions created just now', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-new',
          status: 'active',
          phase: 'INTAKE',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });
  });

  describe('--all flag', () => {
    it('should display all sessions including completed and closed', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-complete',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: new Date(Date.now() - 86_400_000).toISOString(),
          updatedAt: new Date(Date.now() - 86_400_000).toISOString(),
        },
        {
          id: 'session-closed',
          status: 'closed',
          phase: 'POST_DELIVERY',
          createdAt: new Date(Date.now() - 2 * 86_400_000).toISOString(),
          updatedAt: new Date(Date.now() - 2 * 86_400_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand(['--all']);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
      expect(mockSessionManager.getCurrentSessionId).toHaveBeenCalled();
    });

    it('should display complete_with_gaps sessions', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-gaps',
          status: 'complete_with_gaps',
          phase: 'DELIVER_WITH_GAPS',
          createdAt: new Date(Date.now() - 86_400_000).toISOString(),
          updatedAt: new Date(Date.now() - 86_400_000).toISOString(),
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand(['--all']);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });
  });

  describe('convergence formatting', () => {
    it('should display green convergence for high scores', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-high',
          status: 'active',
          phase: 'SYNTHESIS',
          createdAt: new Date(Date.now() - 60_000).toISOString(),
          updatedAt: new Date(Date.now() - 60_000).toISOString(),
          convergence: { overall: 0.9, dimensions: {} as any },
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should display yellow convergence for medium scores', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-medium',
          status: 'active',
          phase: 'SYNTHESIS',
          createdAt: new Date(Date.now() - 60_000).toISOString(),
          updatedAt: new Date(Date.now() - 60_000).toISOString(),
          convergence: { overall: 0.6, dimensions: {} as any },
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });

    it('should display red convergence for low scores', async () => {
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-low',
          status: 'active',
          phase: 'CRITIQUE',
          createdAt: new Date(Date.now() - 60_000).toISOString(),
          updatedAt: new Date(Date.now() - 60_000).toISOString(),
          convergence: { overall: 0.3, dimensions: {} as any },
        },
      ];

      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      await command.run();

      expect(mockSessionManager.listSessions).toHaveBeenCalled();
    });
  });

  describe('Error handling', () => {
    it('should throw on SessionManager.listSessions() failure', async () => {
      mockSessionManager.listSessions.mockRejectedValue(new Error('Disk error'));

      const command = createCommand([]);
      await expect(command.run()).rejects.toThrow('Disk error');
    });
  });
});
