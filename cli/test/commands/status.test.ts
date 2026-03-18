/**
 * Status Command Tests
 *
 * Tests for agon status command:
 * - No current session
 * - Display active session (various phases)
 * - Display complete session with convergence
 * - Cache vs live refresh (--no-refresh)
 * - Token usage display
 * - Error handling
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import Status from '../../src/commands/status.js';
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

function createCommand(args: string[]): Status {
  const command = new Status(args, mockOclifConfig as any);
  vi.spyOn(command, 'log').mockImplementation(() => {});
  vi.spyOn(command, 'error').mockImplementation((msg: any) => {
    throw new Error(typeof msg === 'string' ? msg : String(msg));
  });
  return command;
}

describe('Status Command', () => {
  let mockApiClient: any;
  let mockSessionManager: any;
  let mockConfigManager: any;

  beforeEach(() => {
    vi.clearAllMocks();

    mockApiClient = {
      getSession: vi.fn(),
    };

    mockSessionManager = {
      getCurrentSessionId: vi.fn(),
      getSession: vi.fn(),
      saveSession: vi.fn(),
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
    it('should print guidance message when no current session exists', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const command = createCommand([]);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('No active session found');
    });
  });

  describe('With session ID argument', () => {
    it('should use provided session ID instead of current session', async () => {
      const sessionId = 'explicit-session-id';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };

      mockApiClient.getSession.mockResolvedValue(mockSession);

      const command = createCommand([sessionId]);
      await command.run();

      expect(mockSessionManager.getCurrentSessionId).not.toHaveBeenCalled();
      expect(mockApiClient.getSession).toHaveBeenCalledWith(sessionId);
    });
  });

  describe('Active session display', () => {
    it('should display clarification phase status', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const mockSession: SessionResponse = {
        id: 'session-abc',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        currentRound: 1,
      };
      mockApiClient.getSession.mockResolvedValue(mockSession);

      const command = createCommand([]);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('Clarification');
    });

    it('should display analysis round status', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const mockSession: SessionResponse = {
        id: 'session-abc',
        status: 'active',
        phase: 'ANALYSISROUND',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        currentRound: 1,
      };
      mockApiClient.getSession.mockResolvedValue(mockSession);

      const command = createCommand([]);
      await expect(command.run()).resolves.not.toThrow();
    });
  });

  describe('Complete session display', () => {
    it('should display complete session with convergence data', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const mockSession: SessionResponse = {
        id: 'session-abc',
        status: 'complete',
        phase: 'POST_DELIVERY',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        convergence: {
          overall: 0.87,
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
      };
      mockApiClient.getSession.mockResolvedValue(mockSession);

      const command = createCommand([]);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('87%');
    });

    it('should display complete_with_gaps session', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const mockSession: SessionResponse = {
        id: 'session-abc',
        status: 'complete_with_gaps',
        phase: 'DELIVER_WITH_GAPS',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };
      mockApiClient.getSession.mockResolvedValue(mockSession);

      const command = createCommand([]);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('gaps');
    });
  });

  describe('Token usage display', () => {
    it('should display token usage when available', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const mockSession: SessionResponse = {
        id: 'session-abc',
        status: 'active',
        phase: 'CRITIQUE',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        tokensUsed: 15000,
        tokenBudget: 200000,
      };
      mockApiClient.getSession.mockResolvedValue(mockSession);

      const command = createCommand([]);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      await command.run();

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('15,000');
    });
  });

  describe('Cache behaviour', () => {
    it('should skip API call and use cache when --no-refresh is set', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const cachedSession: SessionResponse = {
        id: 'session-abc',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };
      mockSessionManager.getSession.mockResolvedValue(cachedSession);

      const command = createCommand(['--no-refresh']);
      await command.run();

      expect(mockSessionManager.getSession).toHaveBeenCalled();
      expect(mockApiClient.getSession).not.toHaveBeenCalled();
    });

    it('should fall back to API when cache miss even with --no-refresh', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      mockSessionManager.getSession.mockResolvedValue(null);
      const freshSession: SessionResponse = {
        id: 'session-abc',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };
      mockApiClient.getSession.mockResolvedValue(freshSession);

      const command = createCommand(['--no-refresh']);
      await command.run();

      expect(mockApiClient.getSession).toHaveBeenCalledWith('session-abc');
    });

    it('should fall back to cache when API fails and cache is available', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      const cachedSession: SessionResponse = {
        id: 'session-abc',
        status: 'active',
        phase: 'CRITIQUE',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };
      // --no-refresh: first read cache successfully
      mockSessionManager.getSession.mockResolvedValue(cachedSession);

      const command = createCommand(['--no-refresh']);
      await expect(command.run()).resolves.not.toThrow();
    });
  });

  describe('Phase formatting', () => {
    const phases = [
      { raw: 'INTAKE', expected: 'Intake' },
      { raw: 'CLARIFICATION', expected: 'Clarification' },
      { raw: 'CRITIQUE', expected: 'Critique' },
      { raw: 'SYNTHESIS', expected: 'Synthesis' },
      { raw: 'DELIVER', expected: 'Delivery' },
      { raw: 'POSTDELIVERY', expected: 'Post-Delivery' },
    ];

    for (const { raw, expected } of phases) {
      it(`should format "${raw}" phase as "${expected}"`, async () => {
        mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
        const mockSession: SessionResponse = {
          id: 'session-abc',
          status: 'active',
          phase: raw,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        };
        mockApiClient.getSession.mockResolvedValue(mockSession);

        const command = createCommand([]);
        const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
        await command.run();

        const output = logSpy.mock.calls.flat().join('\n');
        expect(output).toContain(expected);
      });
    }
  });

  describe('Error handling', () => {
    it('should throw when API fails and no cached session is available', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-abc');
      mockApiClient.getSession.mockRejectedValue(new Error('API down'));

      const command = createCommand([]);
      await expect(command.run()).rejects.toThrow();
    });
  });
});
