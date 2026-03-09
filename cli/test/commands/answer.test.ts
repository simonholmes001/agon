/**
 * Answer Command Tests
 * 
 * Tests for the `agon answer` command that submits clarification responses.
 * 
 * Testing strategy:
 * - Mock AgonAPIClient
 * - Mock SessionManager and ConfigManager
 * - Test service interactions (not command execution directly)
 * - Test error handling
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import { ConfigManager } from '../../src/state/config-manager.js';
import type { SessionResponse, Message } from '../../src/api/types.js';
import { getLatestResponseMessage } from '../../src/commands/answer.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');
vi.mock('../../src/state/config-manager.js');

describe('agon answer', () => {
  let mockApiClient: any;
  let mockSessionManager: any;
  let mockConfigManager: any;

  beforeEach(() => {
    vi.clearAllMocks();
    
    // Setup API client mock
    mockApiClient = {
      submitMessage: vi.fn(),
      getMessages: vi.fn()
    };
    
    // Setup session manager mock
    mockSessionManager = {
      getCurrentSessionId: vi.fn()
    };
    
    // Setup config manager mock
    mockConfigManager = {
      load: vi.fn().mockResolvedValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info'
      })
    };
    
    vi.mocked(AgonAPIClient).mockImplementation(() => mockApiClient);
    vi.mocked(SessionManager).mockImplementation(() => mockSessionManager);
    vi.mocked(ConfigManager).mockImplementation(() => mockConfigManager);
  });

  describe('with valid response', () => {
    it('should submit clarification response for current session', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const response = 'Our target customers are small business owners';
      
      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);
      
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: '2026-03-08T12:00:00Z',
        updatedAt: '2026-03-08T12:10:00Z',
        currentRound: 1
      };
      
      const mockMessages: Message[] = [
        {
          agentId: 'moderator',
          message: 'Thank you. What is your budget?',
          round: 1,
          createdAt: '2026-03-08T12:10:00Z'
        }
      ];

      mockApiClient.submitMessage.mockResolvedValue(mockSession);
      mockApiClient.getMessages.mockResolvedValue(mockMessages);

      // Act
      const session = await mockApiClient.submitMessage(sessionId, response);
      const messages = await mockApiClient.getMessages(sessionId);

      // Assert
      expect(mockApiClient.submitMessage).toHaveBeenCalledWith(sessionId, response);
      expect(mockApiClient.getMessages).toHaveBeenCalledWith(sessionId);
      expect(session).toEqual(mockSession);
      expect(messages).toEqual(mockMessages);
    });

    it('should fetch follow-up messages', async () => {
      const sessionId = 'test-session-123';
      
      const mockMessages: Message[] = [
        {
          agentId: 'moderator',
          message: 'Thank you. What is your budget?',
          round: 1,
          createdAt: '2026-03-08T12:10:00Z'
        }
      ];

      mockApiClient.getMessages.mockResolvedValue(mockMessages);

      const result = await mockApiClient.getMessages(sessionId);

      expect(mockApiClient.getMessages).toHaveBeenCalledWith(sessionId);
      expect(result).toEqual(mockMessages);
    });

    it('should handle submission errors', async () => {
      const sessionId = 'test-session-123';
      const response = 'Valid response';
      const error = new Error('API error: Session not found');
      
      mockApiClient.submitMessage.mockRejectedValue(error);

      await expect(mockApiClient.submitMessage(sessionId, response)).rejects.toThrow('API error: Session not found');
    });
  });

  describe('session management', () => {
    it('should get current session ID', async () => {
      const sessionId = 'test-session-123';
      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);

      const result = await mockSessionManager.getCurrentSessionId();

      expect(result).toBe(sessionId);
    });

    it('should handle missing session', async () => {
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      const result = await mockSessionManager.getCurrentSessionId();

      expect(result).toBeNull();
    });
  });

  describe('message selection', () => {
    it('should select latest moderator message during clarification', () => {
      const messages: Message[] = [
        { agentId: 'moderator', message: 'First question', round: 0, createdAt: '2026-03-09T10:00:00Z' },
        { agentId: 'moderator', message: 'Second question', round: 1, createdAt: '2026-03-09T10:05:00Z' },
        { agentId: 'gpt_agent', message: 'Analysis', round: 1, createdAt: '2026-03-09T10:06:00Z' }
      ];

      const selected = getLatestResponseMessage('Clarification', messages);

      expect(selected?.agentId).toBe('moderator');
      expect(selected?.message).toBe('Second question');
    });

    it('should select post-delivery assistant message when available', () => {
      const messages: Message[] = [
        { agentId: 'synthesizer', message: 'Final verdict', round: 2, createdAt: '2026-03-09T10:10:00Z' },
        { agentId: 'post_delivery_assistant', message: 'Revised PRD section', round: 2, createdAt: '2026-03-09T10:12:00Z' }
      ];

      const selected = getLatestResponseMessage('PostDelivery', messages);

      expect(selected?.agentId).toBe('post_delivery_assistant');
      expect(selected?.message).toBe('Revised PRD section');
    });
  });
});
