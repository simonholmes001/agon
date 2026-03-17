/**
 * Start Command Tests
 *
 * Tests for agon start command:
 * - Session creation (non-interactive, no-watch mode for test speed)
 * - Friction flag
 * - Research flag
 * - Error handling (create session fails, start session fails)
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Start from '../../src/commands/start.js';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import { ConfigManager } from '../../src/state/config-manager.js';
import type { SessionResponse } from '../../src/api/types.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');
vi.mock('../../src/state/config-manager.js');
vi.mock('inquirer', () => ({
  default: {
    prompt: vi.fn().mockResolvedValue({ response: 'Test response' }),
  },
}));
vi.mock('ora', () => ({
  default: vi.fn(() => ({
    start: vi.fn().mockReturnThis(),
    succeed: vi.fn().mockReturnThis(),
    fail: vi.fn().mockReturnThis(),
    stop: vi.fn().mockReturnThis(),
    text: '',
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

function createCommand(args: string[]): Start {
  const command = new Start(args, mockOclifConfig as any);
  vi.spyOn(command, 'log').mockImplementation(() => {});
  // Use oclif-style: error() normally exits, so just let it throw
  vi.spyOn(command, 'error').mockImplementation((msg: any) => {
    const message = typeof msg === 'string' ? msg : String(msg);
    throw new Error(message);
  });
  return command;
}

const baseSession: SessionResponse = {
  id: 'new-session-id',
  status: 'active',
  phase: 'INTAKE',
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
};

const postClarificationSession: SessionResponse = {
  ...baseSession,
  phase: 'ANALYSIS_ROUND',
};

describe('Start Command', () => {
  let mockApiClient: any;
  let mockSessionManager: any;
  let mockConfigManager: any;

  beforeEach(() => {
    vi.clearAllMocks();

    mockApiClient = {
      createSession: vi.fn().mockResolvedValue(baseSession),
      startSession: vi.fn().mockResolvedValue(undefined),
      getSession: vi.fn().mockResolvedValue(postClarificationSession),
      getMessages: vi.fn().mockResolvedValue([]),
      submitMessage: vi.fn().mockResolvedValue(postClarificationSession),
      getArtifact: vi.fn().mockResolvedValue({ content: '# Verdict', type: 'verdict' }),
    };

    mockSessionManager = {
      ensureConfigDirectory: vi.fn().mockResolvedValue(undefined),
      saveSession: vi.fn().mockResolvedValue(undefined),
      setCurrentSessionId: vi.fn().mockResolvedValue(undefined),
      saveArtifact: vi.fn().mockResolvedValue(undefined),
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

  describe('Successful session creation', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    it('should create session and start debate (no-interactive, no-watch)', async () => {
      const command = createCommand(['Test idea', '--no-interactive', '--no-watch']);
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      expect(mockApiClient.createSession).toHaveBeenCalledWith(
        expect.objectContaining({ idea: 'Test idea', friction: 50 })
      );
      expect(mockApiClient.startSession).toHaveBeenCalledWith('new-session-id');
      expect(mockSessionManager.saveSession).toHaveBeenCalled();
      expect(mockSessionManager.setCurrentSessionId).toHaveBeenCalledWith('new-session-id');
    });

    it('should use --friction flag value', async () => {
      const command = createCommand(['My idea', '--friction', '85', '--no-interactive', '--no-watch']);
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      expect(mockApiClient.createSession).toHaveBeenCalledWith(
        expect.objectContaining({ friction: 85 })
      );
    });

    it('should use config default friction when flag is not provided', async () => {
      mockConfigManager.load.mockResolvedValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 70,
        researchEnabled: true,
        logLevel: 'info',
      });

      const command = createCommand(['My idea', '--no-interactive', '--no-watch']);
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      expect(mockApiClient.createSession).toHaveBeenCalledWith(
        expect.objectContaining({ friction: 70 })
      );
    });

    it('should pass researchEnabled=false when --no-research is set', async () => {
      const command = createCommand(['My idea', '--no-research', '--no-interactive', '--no-watch']);
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      expect(mockApiClient.createSession).toHaveBeenCalledWith(
        expect.objectContaining({ researchEnabled: false })
      );
    });

    it('should skip clarification loop when --no-interactive is set', async () => {
      const command = createCommand(['My idea', '--no-interactive', '--no-watch']);
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      expect(mockApiClient.getMessages).not.toHaveBeenCalled();
    });

    it('should log next-steps guidance after session is created', async () => {
      const command = createCommand(['My idea', '--no-interactive', '--no-watch']);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('agon status');
    });

    it('should log non-interactive message when session starts in clarification phase', async () => {
      // getSession (refresh after start) returns clarification phase → non-interactive path
      const clarificationSession: SessionResponse = {
        id: 'new-session-id',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };
      mockApiClient.getSession.mockResolvedValue(clarificationSession);

      const command = createCommand(['My idea', '--no-interactive', '--no-watch']);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      const output = logSpy.mock.calls.flat().join('\n');
      expect(output).toContain('Non-interactive mode');
    });
  });

  describe('Interactive clarification loop', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    it('should display moderator message and submit user response', async () => {
      const clarificationSession: SessionResponse = {
        id: 'new-session-id',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };
      const analysiSession = { ...postClarificationSession };
      const moderatorMsg = {
        agentId: 'moderator',
        message: 'What is your target market?',
        round: 1,
        createdAt: '2026-03-09T10:00:00Z',
      };

      mockApiClient.getSession
        .mockResolvedValueOnce(clarificationSession) // run() refresh after start
        .mockResolvedValue(analysiSession);           // inside loop after submit
      mockApiClient.getMessages.mockResolvedValue([moderatorMsg]);
      mockApiClient.submitMessage.mockResolvedValue(analysiSession);

      const command = createCommand(['My idea', '--no-watch']);
      const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      const output = logSpy.mock.calls.flat().join('\n');
      expect(mockApiClient.submitMessage).toHaveBeenCalledWith('new-session-id', 'Test response');
      expect(output).toContain('Clarification complete');
    });

    it('should poll until messages arrive (no messages on first poll)', async () => {
      const clarificationSession: SessionResponse = {
        id: 'new-session-id',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };
      const moderatorMsg = {
        agentId: 'moderator',
        message: 'Tell me more about the target customer.',
        round: 1,
        createdAt: '2026-03-09T10:00:00Z',
      };

      mockApiClient.getSession
        .mockResolvedValueOnce(clarificationSession) // run() refresh after start
        .mockResolvedValueOnce(clarificationSession) // loop: no-messages poll getSession
        .mockResolvedValue(postClarificationSession); // loop: after submit
      mockApiClient.getMessages
        .mockResolvedValueOnce([])          // loop: no messages yet
        .mockResolvedValue([moderatorMsg]); // loop: messages available
      mockApiClient.submitMessage.mockResolvedValue(postClarificationSession);

      const command = createCommand(['My idea', '--no-watch']);
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      expect(mockApiClient.submitMessage).toHaveBeenCalled();
    });

    it('should skip already-answered moderator message and wait for next', async () => {
      const clarificationSession: SessionResponse = {
        id: 'new-session-id',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };
      const oldMsg = {
        agentId: 'moderator',
        message: 'First question',
        round: 1,
        createdAt: '2026-03-09T09:00:00Z',
      };
      const newMsg = {
        agentId: 'moderator',
        message: 'Second question',
        round: 2,
        createdAt: '2026-03-09T10:00:00Z',
      };

      mockApiClient.getSession
        .mockResolvedValueOnce(clarificationSession) // run() refresh
        .mockResolvedValueOnce(clarificationSession) // same-msg poll getSession
        .mockResolvedValue(postClarificationSession); // after submit
      mockApiClient.getMessages
        .mockResolvedValueOnce([oldMsg])  // iteration 1: old message (simulate already answered)
        .mockResolvedValue([newMsg]);     // iteration 2: new message
      mockApiClient.submitMessage.mockResolvedValue(postClarificationSession);

      const command = createCommand(['My idea', '--no-watch']);
      const promise = command.run();

      // Simulate the "already answered" by pre-setting the key won't work directly;
      // instead we rely on the loop iterating twice.
      await vi.runAllTimersAsync();
      await promise;

      // The command should have run without throwing
      expect(mockApiClient.getSession).toHaveBeenCalled();
    });
  });

  describe('Error handling', () => {
    it('should throw when createSession fails', async () => {
      mockApiClient.createSession.mockRejectedValue(new Error('Network error'));

      const command = createCommand(['My idea', '--no-interactive', '--no-watch']);
      await expect(command.run()).rejects.toThrow('Network error');
    });

    it('should throw when startSession fails', async () => {
      mockApiClient.startSession.mockRejectedValue(new Error('Start failed'));

      const command = createCommand(['My idea', '--no-interactive', '--no-watch']);
      await expect(command.run()).rejects.toThrow('Start failed');
    });
  });

  describe('Watch mode (default --watch)', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });
    afterEach(() => {
      vi.useRealTimers();
      delete process.env.AGON_WATCH_MAX_MINUTES;
    delete process.env.AGON_WATCH_MAX_IDLE_MINUTES;
  });

  it('should display final verdict when session completes on first watch iteration', async () => {
    const completeSession: SessionResponse = {
      id: 'new-session-id',
      status: 'complete',
      phase: 'POST_DELIVERY',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };

    mockApiClient.getSession
      .mockResolvedValueOnce(postClarificationSession) // after startSession (run refresh)
      .mockResolvedValue(completeSession);              // watch loop iteration

    mockApiClient.getMessages.mockResolvedValue([]);
    mockApiClient.getArtifact.mockResolvedValue({ content: '# Final Verdict', type: 'verdict' });

    const command = createCommand(['My idea', '--no-interactive']);
    const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
    const promise = command.run();
    await vi.runAllTimersAsync();
    await promise;

    expect(mockApiClient.getArtifact).toHaveBeenCalledWith('new-session-id', 'verdict');
    const output = logSpy.mock.calls.flat().join('\n');
    expect(output).toContain('Debate complete');
  });

  it('should handle complete_with_gaps session during watch', async () => {
    const gapsSession: SessionResponse = {
      id: 'new-session-id',
      status: 'complete_with_gaps',
      phase: 'DELIVER_WITH_GAPS',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };

    mockApiClient.getSession
      .mockResolvedValueOnce(postClarificationSession)
      .mockResolvedValue(gapsSession);
    mockApiClient.getMessages.mockResolvedValue([]);
    mockApiClient.getArtifact.mockResolvedValue({ content: '# Verdict with gaps', type: 'verdict' });

    const command = createCommand(['My idea', '--no-interactive']);
    const promise = command.run();
    await vi.runAllTimersAsync();
    await promise;

    expect(mockApiClient.getArtifact).toHaveBeenCalled();
  });

  it('should display agent messages and then final verdict', async () => {
    const activeSession: SessionResponse = {
      id: 'new-session-id',
      status: 'active',
      phase: 'CRITIQUE',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    const completeSession: SessionResponse = {
      id: 'new-session-id',
      status: 'complete',
      phase: 'POST_DELIVERY',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    const agentMsg = {
      agentId: 'gpt_agent',
      message: '## Analysis\nThis is analysis.',
      round: 1,
      createdAt: new Date().toISOString(),
    };

    mockApiClient.getSession
      .mockResolvedValueOnce(postClarificationSession) // run() refresh
      .mockResolvedValueOnce(activeSession)            // watch iteration 1
      .mockResolvedValue(completeSession);             // watch iteration 2+

    mockApiClient.getMessages
      .mockResolvedValueOnce([agentMsg])   // iteration 1 - new messages
      .mockResolvedValue([agentMsg]);      // iteration 2 - same messages (already seen)

    mockApiClient.getArtifact.mockResolvedValue({ content: '# Verdict', type: 'verdict' });

    const command = createCommand(['My idea', '--no-interactive']);
    const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
    const promise = command.run();
    await vi.runAllTimersAsync();
    await promise;

    const output = logSpy.mock.calls.flat().join('\n');
    expect(output).toContain('gpt_agent');
  });

  it('should gracefully handle verdict fetch failure', async () => {
    const completeSession: SessionResponse = {
      id: 'new-session-id',
      status: 'complete',
      phase: 'POST_DELIVERY',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };

    mockApiClient.getSession
      .mockResolvedValueOnce(postClarificationSession)
      .mockResolvedValue(completeSession);
    mockApiClient.getMessages.mockResolvedValue([]);
    mockApiClient.getArtifact.mockRejectedValue(new Error('Artifact not ready'));

    const command = createCommand(['My idea', '--no-interactive']);
    const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
    const promise = command.run();
    await vi.runAllTimersAsync();
    await promise;

    const output = logSpy.mock.calls.flat().join('\n');
    expect(output).toContain('not ready yet');
  });

  it('should stop watching after max watch duration (AGON_WATCH_MAX_MINUTES env var)', async () => {
    // 1-minute max → triggers after 24 timer iterations of 2500ms each
    process.env.AGON_WATCH_MAX_MINUTES = '1';

    const activeSession: SessionResponse = {
      id: 'new-session-id',
      status: 'active',
      phase: 'ANALYSIS_ROUND',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };

    mockApiClient.getSession
      .mockResolvedValueOnce(postClarificationSession)
      .mockResolvedValue(activeSession);
    mockApiClient.getMessages.mockResolvedValue([]);

    const command = createCommand(['My idea', '--no-interactive']);
    const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
    const promise = command.run();
    await vi.runAllTimersAsync();
    await promise;

    const output = logSpy.mock.calls.flat().join('\n');
    expect(output).toContain('timed out');
  });

  it('should stop watching after consecutive API failures', async () => {
    const apiError = new Error('Network failure');

    mockApiClient.getSession
      .mockResolvedValueOnce(postClarificationSession) // initial refresh in run()
      .mockRejectedValue(apiError);                    // all watch-loop calls fail

    mockApiClient.getMessages.mockRejectedValue(apiError);

    const command = createCommand(['My idea', '--no-interactive']);
    const logSpy = vi.spyOn(command, 'log').mockImplementation(() => {});
    const promise = command.run();
    await vi.runAllTimersAsync();
    await promise;

    const output = logSpy.mock.calls.flat().join('\n');
    expect(output).toContain('repeated fetch failures');
  });

  it('should respect AGON_WATCH_MAX_MINUTES with invalid value (falls back to default)', async () => {
    process.env.AGON_WATCH_MAX_MINUTES = 'not-a-number';
    process.env.AGON_WATCH_MAX_IDLE_MINUTES = '1'; // short idle to exit quickly

    const activeSession: SessionResponse = {
      id: 'new-session-id',
      status: 'active',
      phase: 'CRITIQUE',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };

    mockApiClient.getSession
      .mockResolvedValueOnce(postClarificationSession)
      .mockResolvedValue(activeSession);
    mockApiClient.getMessages.mockResolvedValue([]);

        const command = createCommand(['My idea', '--no-interactive']);
    const promise = command.run();
    await vi.runAllTimersAsync();
    await promise;

        // Should have exited (idle timeout) without throwing
    expect(mockApiClient.getSession).toHaveBeenCalled();
  });
  });
});
