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
      defaults: {
        headers: {
          common: {},
        },
      },
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
      expect(mockAxios.post).toHaveBeenCalledWith(
        '/sessions',
        request,
        expect.objectContaining({ headers: {} })
      );
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

  describe('getUsage', () => {
    it('should retrieve trial usage snapshot', async () => {
      const mockUsage = {
        trialEnabled: true,
        windowStart: '2026-04-01T00:00:00Z',
        windowEnd: '2026-04-08T00:00:00Z',
        quota: {
          tokenLimit: 50000,
          usedTokens: 12000,
          remainingTokens: 38000
        },
        trial: {
          isActive: true,
          expiresAt: '2026-04-15T00:00:00Z',
          globalTrafficEnabled: true
        },
        usageByProviderModel: [
          {
            provider: 'OpenAI',
            model: 'gpt-5',
            totalTokens: 12000,
            promptTokens: 5000,
            completionTokens: 7000
          }
        ]
      };

      (mockAxios.get as any).mockResolvedValue({ data: mockUsage });

      const result = await client.getUsage('2026-04-01T00:00:00Z', '2026-04-08T00:00:00Z');

      expect(mockAxios.get).toHaveBeenCalledWith('/usage', {
        params: {
          from: '2026-04-01T00:00:00Z',
          to: '2026-04-08T00:00:00Z'
        }
      });
      expect(result).toEqual(mockUsage);
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

    it('should map attachment storage-not-configured 503 to actionable backend unavailable message', () => {
      const error: any = new Error('Service unavailable');
      error.response = {
        status: 503,
        data: {
          errorCode: 'ATTACHMENT_STORAGE_NOT_CONFIGURED',
          error: 'Attachment storage is not configured.'
        }
      };

      const mapped = (client as any).mapError(error);
      expect(mapped).toBeInstanceOf(AgonError);
      const agonError = mapped as AgonError;
      expect(agonError.code).toBe(ErrorCode.BACKEND_UNAVAILABLE);
      expect(agonError.message).toBe('Attachment storage is not configured.');
      expect(agonError.suggestions).toContain('Configure blob storage for the backend (BLOB_STORAGE_CONNECTION_STRING)');
    });

    it('should map attachment storage transient 503 to actionable retry guidance', () => {
      const error: any = new Error('Service unavailable');
      error.response = {
        status: 503,
        data: {
          errorCode: 'ATTACHMENT_STORAGE_UNAVAILABLE',
          error: 'Attachment storage is temporarily unavailable.'
        }
      };

      const mapped = (client as any).mapError(error);
      expect(mapped).toBeInstanceOf(AgonError);
      const agonError = mapped as AgonError;
      expect(agonError.code).toBe(ErrorCode.BACKEND_UNAVAILABLE);
      expect(agonError.message).toBe('Attachment storage is temporarily unavailable.');
      expect(agonError.suggestions).toContain('Retry /attach once storage is healthy');
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

    it('should map 401 errors to UNAUTHENTICATED with login guidance', () => {
      const error: any = new Error('Unauthorized');
      error.response = { status: 401, data: {} };
      const mapped = (client as any).mapError(error);
      expect(mapped).toBeInstanceOf(AgonError);
      const agonError = mapped as AgonError;
      expect(agonError.code).toBe(ErrorCode.UNAUTHENTICATED);
      expect(agonError.suggestions).toContain('Run `agon login` to save your bearer token');
    });

    it('should map 403 errors to UNAUTHENTICATED', () => {
      const error: any = new Error('Forbidden');
      error.response = { status: 403, data: {} };
      const mapped = (client as any).mapError(error);
      expect(mapped).toBeInstanceOf(AgonError);
      const agonError = mapped as AgonError;
      expect(agonError.code).toBe(ErrorCode.UNAUTHENTICATED);
    });

    it('should map quota 429 errors to actionable quota guidance', () => {
      const error: any = new Error('Too many requests');
      error.response = {
        status: 429,
        data: {
          error: 'Token quota exceeded for the active trial window.',
          limitType: 'quota',
          windowResetAt: '2026-04-08T00:00:00Z',
          remainingTokens: 0
        },
        headers: {}
      };

      const mapped = (client as any).mapError(error);
      expect(mapped).toBeInstanceOf(AgonError);
      const agonError = mapped as AgonError;
      expect(agonError.code).toBe(ErrorCode.RATE_LIMIT);
      expect(agonError.message).toContain('Token quota exceeded');
      expect(agonError.suggestions).toContain('Run `agon usage` to inspect your remaining quota and reset window');
      expect(agonError.suggestions).toContain('Quota resets at: 2026-04-08T00:00:00Z');
    });

    it('should map rate 429 errors to actionable retry-after guidance', () => {
      const error: any = new Error('Too many requests');
      error.response = {
        status: 429,
        data: {
          error: 'Too many requests for this user. Retry later.',
          limitType: 'rate'
        },
        headers: {
          'retry-after': '7'
        }
      };

      const mapped = (client as any).mapError(error);
      expect(mapped).toBeInstanceOf(AgonError);
      const agonError = mapped as AgonError;
      expect(agonError.code).toBe(ErrorCode.RATE_LIMIT);
      expect(agonError.message).toContain('Too many requests');
      expect(agonError.suggestions).toContain('Retry after approximately 7 second(s)');
    });
  });

  describe('getAuthStatus', () => {
    it('returns auth status from the backend', async () => {
      (mockAxios.get as any).mockResolvedValue({
        data: { required: true, scheme: 'bearer' }
      });

      const result = await client.getAuthStatus();

      expect(mockAxios.get).toHaveBeenCalledWith('/auth/status');
      expect(result).toEqual({ required: true, scheme: 'bearer' });
    });

    it('returns { required: false } when backend does not require auth', async () => {
      (mockAxios.get as any).mockResolvedValue({
        data: { required: false, scheme: 'none' }
      });

      const result = await client.getAuthStatus();

      expect(result?.required).toBe(false);
      expect(result?.scheme).toBe('none');
    });

    it('returns null when the endpoint is unavailable (older backend)', async () => {
      (mockAxios.get as any).mockRejectedValue(new Error('Network error'));

      const result = await client.getAuthStatus();

      expect(result).toBeNull();
    });
  });

  describe('auth token constructor parameter', () => {
    it('uses explicit authToken parameter over env var', () => {
      process.env.AGON_AUTH_TOKEN = 'env-token';
      new AgonAPIClient('http://localhost:5000', '@agon_agents/cli', '0.1.3', 'explicit-token');

      expect(axios.create).toHaveBeenCalledWith(
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: 'Bearer explicit-token'
          })
        })
      );
      delete process.env.AGON_AUTH_TOKEN;
    });

    it('falls back to env var when no explicit token is provided', () => {
      process.env.AGON_AUTH_TOKEN = 'env-token';
      new AgonAPIClient('http://localhost:5000', '@agon_agents/cli', '0.1.3');

      expect(axios.create).toHaveBeenCalledWith(
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: 'Bearer env-token'
          })
        })
      );
      delete process.env.AGON_AUTH_TOKEN;
    });

    it('sends no Authorization header when no token is available', () => {
      delete process.env.AGON_AUTH_TOKEN;
      delete process.env.AGON_BEARER_TOKEN;
      new AgonAPIClient('http://localhost:5000', '@agon_agents/cli', '0.1.3');

      const callArgs = (axios.create as any).mock.calls[0][0];
      expect(callArgs.headers?.Authorization).toBeUndefined();
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
        expect.objectContaining({
          timeout: 120000,
          headers: {}
        })
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

    it('returns actionable error when attachment file is missing', async () => {
      const sessionId = 'session-123';
      const fsError: any = new Error('ENOENT');
      fsError.code = 'ENOENT';
      (fsPromises.stat as any).mockRejectedValue(fsError);

      await expect(client.uploadAttachment(sessionId, './missing.pdf')).rejects.toMatchObject({
        code: ErrorCode.INVALID_INPUT,
        message: 'Attachment file not found: ./missing.pdf'
      });
      expect(mockAxios.post).not.toHaveBeenCalled();
    });

    it('returns actionable error when attachment cannot be read', async () => {
      const sessionId = 'session-123';
      (fsPromises.stat as any).mockResolvedValue({
        isFile: () => true,
        size: 11
      });
      const fsError: any = new Error('EACCES');
      fsError.code = 'EACCES';
      (fsPromises.readFile as any).mockRejectedValue(fsError);

      await expect(client.uploadAttachment(sessionId, './restricted.pdf')).rejects.toMatchObject({
        code: ErrorCode.INVALID_INPUT,
        message: 'Permission denied reading attachment: ./restricted.pdf'
      });
      expect(mockAxios.post).not.toHaveBeenCalled();
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

    it('should not retry attachment upload posts', () => {
      const networkError: any = new Error('Network error');
      networkError.config = { method: 'post', url: '/sessions/test/attachments' };

      const shouldRetry = (client as any).shouldRetry(networkError);

      expect(shouldRetry).toBe(false);
    });
  });

  describe('runtime execution profile headers', () => {
    it('sets user-scope and agent-model headers on client defaults', () => {
      new AgonAPIClient(
        'http://localhost:5000',
        '@agon_agents/cli',
        '0.1.3',
        undefined,
        {
          userScope: 'auth_abc',
          agentModels: {
            moderator: { provider: 'openai', model: 'gpt-5.2' },
            gpt_agent: { provider: 'openai', model: 'gpt-5.2' },
            gemini_agent: { provider: 'google', model: 'gemini-3-flash-preview' },
            claude_agent: { provider: 'anthropic', model: 'claude-opus-4-6' },
            synthesizer: { provider: 'openai', model: 'gpt-5.2' },
            post_delivery_assistant: { provider: 'openai', model: 'gpt-5.2' },
          },
          providerKeys: {
            openai: 'sk-openai',
          },
        }
      );

      expect(axios.create).toHaveBeenCalledWith(
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-Agon-User-Scope': 'auth_abc',
            'X-Agon-Agent-Models': expect.any(String),
          }),
        })
      );

      const callArgs = (axios.create as any).mock.calls[0][0];
      expect(callArgs.headers['X-Agon-Provider-Key-openai']).toBeUndefined();
    });

    it('never sends provider key headers on any requests (server-managed keys only)', async () => {
      // Regression test for issue #381: provider keys are managed server-side.
      // The CLI must not transport them via HTTP headers.
      const runtimeProfile = {
        userScope: 'auth_abc',
        agentModels: {
          moderator: { provider: 'openai', model: 'gpt-5.2' },
          gpt_agent: { provider: 'openai', model: 'gpt-5.2' },
          gemini_agent: { provider: 'google', model: 'gemini-3-flash-preview' },
          claude_agent: { provider: 'anthropic', model: 'claude-opus-4-6' },
          synthesizer: { provider: 'openai', model: 'gpt-5.2' },
          post_delivery_assistant: { provider: 'openai', model: 'gpt-5.2' },
        },
        providerKeys: {
          openai: 'sk-openai',
          anthropic: 'sk-anth',
        },
      } as const;

      const runtimeClient = new AgonAPIClient(
        'http://localhost:5000',
        '@agon_agents/cli',
        '0.1.3',
        undefined,
        runtimeProfile
      );
      (mockAxios.post as any).mockResolvedValue({
        data: {
          id: 'session-1',
          status: 'active',
          phase: 'INTAKE',
          createdAt: '2026-03-07T10:00:00Z',
          updatedAt: '2026-03-07T10:00:00Z',
        },
      });

      await runtimeClient.createSession({
        idea: 'A sufficiently long product idea for validation',
        friction: 60,
        researchEnabled: true,
      });

      const callArgs = (mockAxios.post as any).mock.calls[0];
      const headers = callArgs[2]?.headers ?? {};

      // Provider key headers must NEVER be sent
      Object.keys(headers).forEach((key) => {
        expect(key.toLowerCase()).not.toMatch(/^x-agon-provider-key-/);
      });
    });
  });
});
