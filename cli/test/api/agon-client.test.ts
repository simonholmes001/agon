/**
 * API Client Tests
 * 
 * Testing strategy:
 * - Mock axios to avoid real HTTP calls
 * - Test error handling and retries
 * - Test request/response transformation
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import axios from 'axios';
import type { AxiosInstance } from 'axios';
import * as fsPromises from 'node:fs/promises';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { AgonError, ErrorCode } from '../../src/utils/error-handler.js';
import type { CreateSessionRequest, SessionResponse } from '../../src/api/types.js';

vi.mock('axios');
vi.mock('node:fs/promises', () => ({
  readFile: vi.fn(),
  stat: vi.fn()
}));

describe('AgonAPIClient', () => {
  let client: AgonAPIClient;
  let mockAxios: AxiosInstance;

  beforeEach(() => {
    vi.resetAllMocks();
    delete process.env.AGON_AUTH_TOKEN;
    delete process.env.AGON_BEARER_TOKEN;

    mockAxios = {
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
      interceptors: {
        request: { use: vi.fn(), eject: vi.fn(), clear: vi.fn() },
        response: { use: vi.fn(), eject: vi.fn(), clear: vi.fn() }
      }
    } as any;

    (axios.create as any).mockReturnValue(mockAxios);
    
    client = new AgonAPIClient('http://localhost:5000', '@agon_agents/cli', '0.1.3');
  });

  describe('createSession', () => {
    it('should create a session with valid request', async () => {
      // Arrange
      const request: CreateSessionRequest = {
        idea: 'Build a SaaS platform',
        friction: 50,
        researchEnabled: true
      };

      const mockResponse: SessionResponse = {
        id: 'test-session-123',
        status: 'active',
        phase: 'INTAKE',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T10:00:00Z'
      };

      (mockAxios.post as any).mockResolvedValue({ data: mockResponse });

      // Act
      const result = await client.createSession(request);

      // Assert
      expect(mockAxios.post).toHaveBeenCalledWith('/sessions', request);
      expect(result).toEqual(mockResponse);
    });

    it('should throw error when idea is empty', async () => {
      // Arrange
      const request: CreateSessionRequest = {
        idea: '',
        friction: 50
      };

      // Act & Assert
      await expect(client.createSession(request)).rejects.toThrow('Idea cannot be empty');
    });

    it('should throw error when idea is too short', async () => {
      // Arrange
      const request: CreateSessionRequest = {
        idea: 'test',
        friction: 50
      };

      // Act & Assert
      await expect(client.createSession(request)).rejects.toThrow('Idea must be at least 10 characters');
    });
  });

  describe('getSession', () => {
    it('should retrieve session by ID', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const mockResponse: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T10:05:00Z',
        currentRound: 1
      };

      (mockAxios.get as any).mockResolvedValue({ data: mockResponse });

      // Act
      const result = await client.getSession(sessionId);

      // Assert
      expect(mockAxios.get).toHaveBeenCalledWith(`/sessions/${sessionId}`);
      expect(result).toEqual(mockResponse);
    });

    it('should throw error for non-existent session', async () => {
      // Arrange
      const sessionId = 'non-existent';
      const error: any = new Error('Request failed with status code 404');
      error.response = { 
        status: 404, 
        data: { message: 'Session not found' } 
      };
      error.isAxiosError = true;
      
      (mockAxios.get as any).mockRejectedValue(error);

      // Act & Assert
      await expect(client.getSession(sessionId)).rejects.toThrow();
    });
  });

  describe('listSessions', () => {
    it('should retrieve sessions for the current user', async () => {
      const mockResponse: SessionResponse[] = [
        {
          id: 'session-1',
          status: 'active',
          phase: 'Clarification',
          createdAt: '2026-03-07T10:00:00Z',
          updatedAt: '2026-03-07T10:05:00Z'
        },
        {
          id: 'session-2',
          status: 'complete',
          phase: 'Deliver',
          createdAt: '2026-03-06T10:00:00Z',
          updatedAt: '2026-03-06T11:00:00Z'
        }
      ];

      (mockAxios.get as any).mockResolvedValue({ data: mockResponse });

      const result = await client.listSessions();

      expect(mockAxios.get).toHaveBeenCalledWith('/sessions');
      expect(result).toEqual(mockResponse);
    });
  });

  describe('error handling', () => {
    it('should handle network errors gracefully', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const error: any = new Error('Network error');
      error.isAxiosError = true;
      (mockAxios.get as any).mockRejectedValue(error);

      // Act & Assert
      await expect(client.getSession(sessionId)).rejects.toThrow();
    });

    it('should handle 500 errors', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const error: any = new Error('Internal server error');
      error.response = { status: 500, data: {} };
      error.isAxiosError = true;
      (mockAxios.get as any).mockRejectedValue(error);

      // Act & Assert
      await expect(client.getSession(sessionId)).rejects.toThrow();
    });

    it('should map 504 errors to backend unavailable', () => {
      const error: any = new Error('Gateway Timeout');
      error.response = { status: 504, data: {} };
      const mapped = (client as any).mapError(error);
      expect(mapped).toBeInstanceOf(AgonError);
      const agonError = mapped as AgonError;
      expect(agonError.code).toBe(ErrorCode.BACKEND_UNAVAILABLE);
    });

    it('should map 426 errors to CLI upgrade required', async () => {
      const error: any = new Error('Request failed with status code 426');
      error.response = {
        status: 426,
        data: { detail: 'Please upgrade Agon CLI.' }
      };
      const mapped = (client as any).mapError(error);
      expect(mapped).toBeInstanceOf(AgonError);
      const agonError = mapped as AgonError;
      expect(agonError.code).toBe(ErrorCode.CLI_UPGRADE_REQUIRED);
      expect(agonError.suggestions).toContain('Run `agon --self-update`');
    });
  });

  describe('timeout handling', () => {
    it('should timeout after 30 seconds', async () => {
      // This test verifies the axios config, not actual timeout behavior
      expect(axios.create).toHaveBeenCalledWith(
        expect.objectContaining({
          timeout: 30000
        })
      );
    });

    it('should set CLI version headers for backend policy enforcement', async () => {
      expect(axios.create).toHaveBeenCalledWith(
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-Agon-CLI-Version': '0.1.3',
            'User-Agent': '@agon_agents/cli/0.1.3'
          })
        })
      );
    });

    it('should include bearer token header when AGON_AUTH_TOKEN is set', async () => {
      process.env.AGON_AUTH_TOKEN = 'test-token';
      new AgonAPIClient('http://localhost:5000', '@agon_agents/cli', '0.1.3');

      expect(axios.create).toHaveBeenCalledWith(
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: 'Bearer test-token'
          })
        })
      );
    });
  });

  describe('getMessages', () => {
    it('should fetch conversation messages for a session', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const mockMessages = [
        {
          agentId: 'moderator',
          message: 'What is your target customer?',
          round: 0,
          createdAt: '2026-03-08T12:00:00Z'
        },
        {
          agentId: 'user',
          message: 'Small business owners',
          round: 0,
          createdAt: '2026-03-08T12:05:00Z'
        }
      ];

      (mockAxios.get as any).mockResolvedValue({ data: mockMessages });

      // Act
      const result = await client.getMessages(sessionId);

      // Assert
      expect(mockAxios.get).toHaveBeenCalledWith(`/sessions/${sessionId}/messages`);
      expect(result).toEqual(mockMessages);
    });

    it('should return empty array when no messages exist', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      (mockAxios.get as any).mockResolvedValue({ data: [] });

      // Act
      const result = await client.getMessages(sessionId);

      // Assert
      expect(result).toEqual([]);
    });

    it('should handle errors when fetching messages', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const error: any = new Error('Request failed with status code 404');
      error.response = { 
        status: 404, 
        data: { message: 'Session not found' } 
      };
      error.isAxiosError = true;
      
      (mockAxios.get as any).mockRejectedValue(error);

      // Act & Assert
      await expect(client.getMessages(sessionId)).rejects.toThrow();
    });
  });

  describe('submitMessage', () => {
    it('should submit a clarification response', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const content = 'Our target customers are small business owners aged 25-50';
      
      const mockResponse: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: '2026-03-08T12:00:00Z',
        updatedAt: '2026-03-08T12:10:00Z',
        currentRound: 1
      };

      (mockAxios.post as any).mockResolvedValue({ data: mockResponse });

      // Act
      const result = await client.submitMessage(sessionId, content);

      // Assert
      expect(mockAxios.post).toHaveBeenCalledWith(
        `/sessions/${sessionId}/messages`,
        { content },
        expect.objectContaining({ timeout: 120000 })
      );
      expect(result).toEqual(mockResponse);
    });

    it('should reject empty content', async () => {
      // Arrange
      const sessionId = 'test-session-123';

      // Act & Assert
      await expect(client.submitMessage(sessionId, '')).rejects.toThrow('Message content cannot be empty');
      await expect(client.submitMessage(sessionId, '   ')).rejects.toThrow('Message content cannot be empty');
    });

    it('should handle submission errors', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const content = 'Valid response';
      const error: any = new Error('Request failed with status code 400');
      error.response = { 
        status: 400, 
        data: { message: 'Invalid request' } 
      };
      error.isAxiosError = true;
      
      (mockAxios.post as any).mockRejectedValue(error);

      // Act & Assert
      await expect(client.submitMessage(sessionId, content)).rejects.toThrow();
    });
  });

  describe('uploadAttachment', () => {
    it('uploads a local file as multipart form-data', async () => {
      const sessionId = 'session-123';
      (fsPromises.readFile as any).mockResolvedValue(Buffer.from('hello world'));
      (fsPromises.stat as any).mockResolvedValue({
        isFile: () => true,
        size: 11
      });

      const attachment = {
        id: 'att-1',
        sessionId,
        fileName: 'brief.md',
        contentType: 'text/markdown',
        sizeBytes: 11,
        accessUrl: 'https://example.test/brief.md',
        uploadedAt: '2026-03-16T12:00:00Z',
        hasExtractedText: true
      };
      (mockAxios.post as any).mockResolvedValue({ data: attachment });

      const result = await client.uploadAttachment(sessionId, './brief.md');

      expect(mockAxios.post).toHaveBeenCalledWith(
        `/sessions/${sessionId}/attachments`,
        expect.any(FormData),
        expect.objectContaining({ timeout: 120000 })
      );
      expect(result).toEqual(attachment);
    });
  });

  describe('retry policy', () => {
    it('should not retry timeout errors', () => {
      const timeoutError: any = new Error('timeout of 30000ms exceeded');
      timeoutError.code = 'ECONNABORTED';
      timeoutError.config = { method: 'post', url: '/sessions/test/messages' };

      const shouldRetry = (client as any).shouldRetry(timeoutError);

      expect(shouldRetry).toBe(false);
    });

    it('should not retry message submission posts', () => {
      const networkError: any = new Error('Network error');
      networkError.config = { method: 'post', url: '/sessions/test/messages' };

      const shouldRetry = (client as any).shouldRetry(networkError);

      expect(shouldRetry).toBe(false);
    });
  });
});
