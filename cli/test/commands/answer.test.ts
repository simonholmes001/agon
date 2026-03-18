/**
 * Answer Command Tests
 *
 * Tests for the `agon answer` / `agon follow-up` command:
 * - Submitting a response for the current session
 * - Resolving session ID (explicit, current, latest)
 * - Post-delivery path
 * - Clarification path
 * - Helper function unit tests
 * - Error handling
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import { ConfigManager } from '../../src/state/config-manager.js';
import type { SessionResponse, Message } from '../../src/api/types.js';
import Answer from '../../src/commands/answer.js';
import {
  getLatestPostDeliveryAssistantMessage,
  getLatestResponseMessage,
} from '../../src/commands/answer.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');
vi.mock('../../src/state/config-manager.js');
vi.mock('ora', () => ({
  default: vi.fn(() => ({
    start: vi.fn().mockReturnThis(),
    succeed: vi.fn().mockReturnThis(),
    fail: vi.fn().mockReturnThis(),
    stop: vi.fn().mockReturnThis(),
    text: '',
  })),
}));
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

function createCommand(args: string[]): Answer {
  const command = new Answer(args, mockOclifConfig as any);
  return command;
}

describe('agon answer', () => {
  let mockApiClient: any;
  let mockSessionManager: any;
  let mockConfigManager: any;

  beforeEach(() => {
    vi.clearAllMocks();

    mockApiClient = {
      submitMessage: vi.fn(),
      getMessages: vi.fn(),
      getArtifact: vi.fn(),
    };

    mockSessionManager = {
      getCurrentSessionId: vi.fn(),
      listSessions: vi.fn(),
      setCurrentSessionId: vi.fn(),
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

  it('should expose the follow-up command alias', () => {
    expect(Answer.aliases).toContain('follow-up');
  });

  describe('Command execution — clarification phase', () => {
    it('should submit message and render moderator response in clarification phase', async () => {
      const sessionId = 'test-session-123';
      const clarificationSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'Clarification',
        createdAt: '2026-03-08T12:00:00Z',
        updatedAt: '2026-03-08T12:10:00Z',
        currentRound: 1,
      };
      const messages: Message[] = [
        {
          agentId: 'moderator',
          message: 'What is your budget?',
          round: 1,
          createdAt: '2026-03-08T12:10:00Z',
        },
      ];

      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);
      mockApiClient.getMessages.mockResolvedValue([]);
      mockApiClient.submitMessage.mockResolvedValue(clarificationSession);
      mockApiClient.getMessages.mockResolvedValueOnce([]).mockResolvedValue(messages);
      mockSessionManager.saveSession.mockResolvedValue(undefined);
      mockSessionManager.setCurrentSessionId.mockResolvedValue(undefined);

      const command = createCommand(['Our budget is $50k']);
      await expect(command.run()).resolves.not.toThrow();

      expect(mockApiClient.submitMessage).toHaveBeenCalledWith(sessionId, 'Our budget is $50k');
    });

    it('should handle no moderator response in clarification phase', async () => {
      const sessionId = 'test-session-123';
      const clarificationSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'Clarification',
        createdAt: '2026-03-08T12:00:00Z',
        updatedAt: '2026-03-08T12:10:00Z',
      };

      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);
      mockApiClient.getMessages.mockResolvedValue([]);
      mockApiClient.submitMessage.mockResolvedValue(clarificationSession);
      mockSessionManager.saveSession.mockResolvedValue(undefined);

      const command = createCommand(['My answer']);
      await expect(command.run()).resolves.not.toThrow();
    });
  });

  describe('Command execution — post-delivery phase', () => {
    it('should render assistant response in post-delivery phase', async () => {
      const sessionId = 'test-session-123';
      const postDeliverySession: SessionResponse = {
        id: sessionId,
        status: 'complete',
        phase: 'PostDelivery',
        createdAt: '2026-03-08T12:00:00Z',
        updatedAt: '2026-03-08T12:10:00Z',
      };
      const assistantMessage: Message = {
        agentId: 'post_delivery_assistant',
        message: 'Here is the revised PRD section',
        round: 2,
        createdAt: '2026-03-08T12:15:00Z',
      };

      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);
      mockApiClient.getMessages
        .mockResolvedValueOnce([]) // before submit
        .mockResolvedValue([assistantMessage]); // after submit
      mockApiClient.submitMessage.mockResolvedValue(postDeliverySession);
      mockSessionManager.saveSession.mockResolvedValue(undefined);

      const command = createCommand(['Please revise the PRD']);
      await expect(command.run()).resolves.not.toThrow();
    });

    it('should show waiting message when no immediate post-delivery response', async () => {
      vi.useFakeTimers();

      const sessionId = 'test-session-123';
      const postDeliverySession: SessionResponse = {
        id: sessionId,
        status: 'complete',
        phase: 'PostDelivery',
        createdAt: '2026-03-08T12:00:00Z',
        updatedAt: '2026-03-08T12:10:00Z',
      };

      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);
      mockApiClient.getMessages.mockResolvedValue([]);
      mockApiClient.submitMessage.mockResolvedValue(postDeliverySession);
      mockSessionManager.saveSession.mockResolvedValue(undefined);

      const command = createCommand(['Follow-up question']);
      const promise = command.run();
      await vi.runAllTimersAsync();
      await promise;

      vi.useRealTimers();
    });
  });

  describe('Command execution — debate phase (non-clarification, non-post-delivery)', () => {
    it('should print debate-started message for analysis phase', async () => {
      const sessionId = 'test-session-123';
      const analysisSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'AnalysisRound',
        createdAt: '2026-03-08T12:00:00Z',
        updatedAt: '2026-03-08T12:10:00Z',
      };

      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);
      mockApiClient.getMessages.mockResolvedValue([]);
      mockApiClient.submitMessage.mockResolvedValue(analysisSession);
      mockSessionManager.saveSession.mockResolvedValue(undefined);

      const command = createCommand(['Response text']);
      await expect(command.run()).resolves.not.toThrow();
    });
  });

  describe('--session flag', () => {
    it('should use explicit session ID from --session flag', async () => {
      const explicitId = 'explicit-session-id';
      const session: SessionResponse = {
        id: explicitId,
        status: 'active',
        phase: 'Clarification',
        createdAt: '2026-03-08T12:00:00Z',
        updatedAt: '2026-03-08T12:10:00Z',
      };

      mockApiClient.getMessages.mockResolvedValue([]);
      mockApiClient.submitMessage.mockResolvedValue(session);
      mockSessionManager.setCurrentSessionId.mockResolvedValue(undefined);
      mockSessionManager.saveSession.mockResolvedValue(undefined);

      const command = createCommand(['My answer', '--session', explicitId]);
      await command.run();

      expect(mockSessionManager.setCurrentSessionId).toHaveBeenCalledWith(explicitId);
      expect(mockApiClient.submitMessage).toHaveBeenCalledWith(explicitId, 'My answer');
    });
  });

  describe('Session resolution — no current session', () => {
    it('should fall back to latest cached session when current session is not set', async () => {
      const sessions: SessionResponse[] = [
        {
          id: 'older-session',
          status: 'complete',
          phase: 'PostDelivery',
          createdAt: '2026-03-09T10:00:00Z',
          updatedAt: '2026-03-09T10:10:00Z',
        },
        {
          id: 'latest-session',
          status: 'complete',
          phase: 'PostDelivery',
          createdAt: '2026-03-09T11:00:00Z',
          updatedAt: '2026-03-09T11:30:00Z',
        },
      ];

      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);
      mockSessionManager.listSessions.mockResolvedValue(sessions);
      mockSessionManager.setCurrentSessionId.mockResolvedValue(undefined);
      mockSessionManager.saveSession.mockResolvedValue(undefined);

      const postDeliverySession: SessionResponse = {
        id: 'latest-session',
        status: 'complete',
        phase: 'PostDelivery',
        createdAt: '2026-03-09T11:00:00Z',
        updatedAt: '2026-03-09T11:30:00Z',
      };
      const assistantMsg: Message = {
        agentId: 'post_delivery_assistant',
        message: 'Reply',
        round: 2,
        createdAt: '2026-03-09T11:31:00Z',
      };

      mockApiClient.getMessages
        .mockResolvedValueOnce([])
        .mockResolvedValue([assistantMsg]);
      mockApiClient.submitMessage.mockResolvedValue(postDeliverySession);

      const command = createCommand(['Follow-up']);
      await expect(command.run()).resolves.not.toThrow();

      expect(mockSessionManager.setCurrentSessionId).toHaveBeenCalledWith('latest-session');
    });

    it('should exit when no session is found at all', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);
      mockSessionManager.listSessions.mockResolvedValue([]);

      const command = createCommand(['My answer']);
      const exitSpy = vi.spyOn(command, 'exit').mockImplementation((code?: number) => {
        throw new Error(`exit(${code})`);
      });

      await expect(command.run()).rejects.toThrow('exit(1)');
      expect(exitSpy).toHaveBeenCalledWith(1);
    });
  });

  describe('Error handling', () => {
    it('should exit with code 1 on submitMessage failure', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue('session-123');
      mockApiClient.getMessages.mockResolvedValue([]);
      mockApiClient.submitMessage.mockRejectedValue(new Error('Submit failed'));

      const command = createCommand(['Answer']);
      const exitSpy = vi.spyOn(command, 'exit').mockImplementation((code?: number) => {
        throw new Error(`exit(${code})`);
      });

      await expect(command.run()).rejects.toThrow('exit(1)');
      expect(exitSpy).toHaveBeenCalledWith(1);
    });
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Pure-function unit tests (same as before)
  // ────────────────────────────────────────────────────────────────────────────

  describe('message selection helpers', () => {
    it('should select latest moderator message during clarification', () => {
      const messages: Message[] = [
        { agentId: 'moderator', message: 'First question', round: 0, createdAt: '2026-03-09T10:00:00Z' },
        { agentId: 'moderator', message: 'Second question', round: 1, createdAt: '2026-03-09T10:05:00Z' },
        { agentId: 'gpt_agent', message: 'Analysis', round: 1, createdAt: '2026-03-09T10:06:00Z' },
      ];

      const selected = getLatestResponseMessage('Clarification', messages);

      expect(selected?.agentId).toBe('moderator');
      expect(selected?.message).toBe('Second question');
    });

    it('should select post-delivery assistant message when available', () => {
      const messages: Message[] = [
        { agentId: 'synthesizer', message: 'Final verdict', round: 2, createdAt: '2026-03-09T10:10:00Z' },
        { agentId: 'post_delivery_assistant', message: 'Revised PRD section', round: 2, createdAt: '2026-03-09T10:12:00Z' },
      ];

      const selected = getLatestResponseMessage('PostDelivery', messages);

      expect(selected?.agentId).toBe('post_delivery_assistant');
      expect(selected?.message).toBe('Revised PRD section');
    });

    it('should not fall back to stale non-assistant messages in post-delivery mode', () => {
      const messages: Message[] = [
        { agentId: 'synthesizer', message: 'Final verdict', round: 2, createdAt: '2026-03-09T10:10:00Z' },
        { agentId: 'gpt_agent', message: 'Analysis output', round: 2, createdAt: '2026-03-09T10:11:00Z' },
      ];

      const selected = getLatestResponseMessage('PostDelivery', messages);

      expect(selected).toBeUndefined();
    });

    it('should ignore user-authored messages when selecting latest debate response', () => {
      const messages: Message[] = [
        { agentId: 'gpt_agent', message: 'Previous analysis', round: 0, createdAt: '2026-03-09T10:10:00Z' },
        { agentId: 'user', message: 'My follow-up answer', round: 0, createdAt: '2026-03-09T10:11:00Z' },
      ];

      const selected = getLatestResponseMessage('AnalysisRound', messages);

      expect(selected?.agentId).toBe('gpt_agent');
      expect(selected?.message).toBe('Previous analysis');
    });

    it('should return only new post-delivery assistant messages after a known timestamp', () => {
      const messages: Message[] = [
        { agentId: 'post_delivery_assistant', message: 'Older reply', round: 2, createdAt: '2026-03-09T10:12:00Z' },
        { agentId: 'post_delivery_assistant', message: 'New reply', round: 2, createdAt: '2026-03-09T10:15:00Z' },
      ];

      const selected = getLatestPostDeliveryAssistantMessage(messages, '2026-03-09T10:13:00Z');

      expect(selected?.message).toBe('New reply');
    });
  });
});
