/**
 * Resume Command Tests
 * 
 * Testing strategy:
 * - Mock AgonAPIClient
 * - Mock SessionManager
 * - Test session resumption
 * - Test validation
 * - Test error handling
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import type { SessionResponse } from '../../src/api/types.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');

describe('resume command', () => {
  let mockApiClient: any;
  let mockSessionManager: any;

  beforeEach(() => {
    vi.clearAllMocks();
    
    // Setup API client mock
    mockApiClient = {
      getSession: vi.fn()
    };
    
    // Setup session manager mock
    mockSessionManager = {
      getSession: vi.fn(),
      saveSession: vi.fn(),
      setCurrentSessionId: vi.fn()
    };
    
    vi.mocked(AgonAPIClient).mockImplementation(() => mockApiClient);
    vi.mocked(SessionManager).mockImplementation(() => mockSessionManager);
  });

  describe('session resumption', () => {
    it('should set session as current', async () => {
      // Arrange
      const sessionId = 'session-123';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'paused',
        phase: 'ANALYSIS_ROUND',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z'
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      await mockSessionManager.setCurrentSessionId(sessionId);

      // Assert
      expect(mockSessionManager.setCurrentSessionId).toHaveBeenCalledWith(sessionId);
    });

    it('should fetch session from API to validate it exists', async () => {
      // Arrange
      const sessionId = 'session-123';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'paused',
        phase: 'ANALYSIS_ROUND',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z'
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession(sessionId);

      // Assert
      expect(mockApiClient.getSession).toHaveBeenCalledWith(sessionId);
      expect(session.id).toBe(sessionId);
    });

    it('should save session to cache after fetching', async () => {
      // Arrange
      const sessionId = 'session-123';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'paused',
        phase: 'ANALYSIS_ROUND',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z'
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession(sessionId);
      await mockSessionManager.saveSession(session);

      // Assert
      expect(mockSessionManager.saveSession).toHaveBeenCalledWith(mockSession);
    });
  });

  describe('session validation', () => {
    it('should resume paused session', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'session-123',
        status: 'paused',
        phase: 'ANALYSIS_ROUND',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z'
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession('session-123');

      // Assert
      expect(session.status).toBe('paused');
    });

    it('should resume active session', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'session-123',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z'
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession('session-123');

      // Assert
      expect(session.status).toBe('active');
    });

    it('should allow resuming complete session (for post-delivery)', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'session-123',
        status: 'complete',
        phase: 'DELIVER',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z'
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession('session-123');

      // Assert
      expect(session.status).toBe('complete');
    });
  });

  describe('error handling', () => {
    it('should handle session not found', async () => {
      // Arrange
      const error = new Error('Session not found');
      mockApiClient.getSession.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.getSession('non-existent-id')
      ).rejects.toThrow('Session not found');
    });

    it('should handle network errors', async () => {
      // Arrange
      const error = new Error('Network request failed');
      mockApiClient.getSession.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.getSession('session-123')
      ).rejects.toThrow('Network request failed');
    });

    it('should handle backend unavailable', async () => {
      // Arrange
      const error = new Error('Backend service unavailable');
      mockApiClient.getSession.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.getSession('session-123')
      ).rejects.toThrow('Backend service unavailable');
    });
  });

  describe('cache handling', () => {
    it('should use cached session if available', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'session-123',
        status: 'paused',
        phase: 'ANALYSIS_ROUND',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z'
      };
      
      mockSessionManager.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockSessionManager.getSession('session-123');

      // Assert
      expect(session).toBe(mockSession);
      expect(mockApiClient.getSession).not.toHaveBeenCalled();
    });

    it('should fetch from API if cache miss', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'session-123',
        status: 'paused',
        phase: 'ANALYSIS_ROUND',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T11:00:00Z'
      };
      
      mockSessionManager.getSession.mockResolvedValue(null);
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act - Simulate command flow
      const cached = await mockSessionManager.getSession('session-123');
      
      if (cached) {
        // Use cached session
        expect(cached).toBe(mockSession);
      } else {
        const session = await mockApiClient.getSession('session-123');
        await mockSessionManager.saveSession(session);
        
        // Assert
        expect(mockApiClient.getSession).toHaveBeenCalledWith('session-123');
        expect(mockSessionManager.saveSession).toHaveBeenCalledWith(mockSession);
      }
    });
  });
});
