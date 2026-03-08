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
import { AgonAPIClient } from '../../src/api/agon-client.js';
import type { CreateSessionRequest, SessionResponse } from '../../src/api/types.js';

vi.mock('axios');

describe('AgonAPIClient', () => {
  let client: AgonAPIClient;
  let mockAxios: AxiosInstance;

  beforeEach(() => {
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
    
    client = new AgonAPIClient('http://localhost:5000');
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
        { content }
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
});
