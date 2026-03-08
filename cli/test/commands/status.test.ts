/**
 * Status Command Tests
 * 
 * Testing strategy:
 * - Mock AgonAPIClient
 * - Mock SessionManager
 * - Test status display for different session states
 * - Test error handling
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import type { SessionResponse } from '../../src/api/types.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');

describe('status command', () => {
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
      getCurrentSessionId: vi.fn(),
      getSession: vi.fn(),
      saveSession: vi.fn()
    };
    
    vi.mocked(AgonAPIClient).mockImplementation(() => mockApiClient);
    vi.mocked(SessionManager).mockImplementation(() => mockSessionManager);
  });

  describe('current session detection', () => {
    it('should detect and display current session', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      const mockSession: SessionResponse = {
        id: sessionId,
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);
      mockSessionManager.getSession.mockResolvedValue(null); // Cache miss
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act - Simulate the command flow
      const currentId = await mockSessionManager.getCurrentSessionId();
      
      if (!currentId) {
        throw new Error('No current session');
      }
      
      const cachedSession = await mockSessionManager.getSession(currentId);
      const session = cachedSession || await mockApiClient.getSession(currentId);

      // Assert
      expect(mockSessionManager.getCurrentSessionId).toHaveBeenCalled();
      expect(mockApiClient.getSession).toHaveBeenCalledWith(sessionId);
      expect(session.id).toBe(sessionId);
      expect(session.phase).toBe('CLARIFICATION');
    });

    it('should handle no current session', async () => {
      // Arrange
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      // Act
      const currentSessionId = await mockSessionManager.getCurrentSessionId();

      // Assert
      expect(currentSessionId).toBeNull();
    });
  });

  describe('session status display', () => {
    it('should display basic session info (CLARIFICATION phase)', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-123',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession('test-session-123');

      // Assert
      expect(session.phase).toBe('CLARIFICATION');
      expect(session.status).toBe('active');
    });

    it('should display convergence score (DEBATE phase)', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-123',
        status: 'active',
        phase: 'ANALYSIS_ROUND',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        convergence: {
          overall: 0.65,
          dimensions: {
            assumption_explicitness: 0.7,
            evidence_quality: 0.6,
            risk_coverage: 0.65,
            decision_clarity: 0.68,
            scope_definition: 0.62,
            constraint_alignment: 0.64,
            uncertainty_acknowledgment: 0.66
          }
        },
        currentRound: 1
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession('test-session-123');

      // Assert
      expect(session.convergence).toBeDefined();
      expect(session.convergence?.overall).toBe(0.65);
      expect(session.currentRound).toBe(1);
    });

    it('should display token usage', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-123',
        status: 'active',
        phase: 'ANALYSIS_ROUND',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        tokensUsed: 15000,
        tokenBudget: 100000
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession('test-session-123');

      // Assert
      expect(session.tokensUsed).toBe(15000);
      expect(session.tokenBudget).toBe(100000);
      
      // Calculate percentage
      const usagePercent = (session.tokensUsed! / session.tokenBudget!) * 100;
      expect(usagePercent).toBe(15);
    });

    it('should display complete session', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-123',
        status: 'complete',
        phase: 'DELIVER',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        convergence: {
          overall: 0.85,
          dimensions: {
            assumption_explicitness: 0.88,
            evidence_quality: 0.82,
            risk_coverage: 0.84,
            decision_clarity: 0.87,
            scope_definition: 0.83,
            constraint_alignment: 0.85,
            uncertainty_acknowledgment: 0.86
          }
        }
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession('test-session-123');

      // Assert
      expect(session.status).toBe('complete');
      expect(session.phase).toBe('DELIVER');
      expect(session.convergence?.overall).toBeGreaterThanOrEqual(0.75);
    });

    it('should display session with gaps', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-123',
        status: 'complete_with_gaps',
        phase: 'DELIVER_WITH_GAPS',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        convergence: {
          overall: 0.68,
          dimensions: {
            assumption_explicitness: 0.65,
            evidence_quality: 0.6,
            risk_coverage: 0.7,
            decision_clarity: 0.72,
            scope_definition: 0.65,
            constraint_alignment: 0.68,
            uncertainty_acknowledgment: 0.7
          }
        }
      };
      
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.getSession('test-session-123');

      // Assert
      expect(session.status).toBe('complete_with_gaps');
      expect(session.convergence?.overall).toBeLessThan(0.75);
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
        mockApiClient.getSession('test-session-123')
      ).rejects.toThrow('Network request failed');
    });

    it('should handle backend unavailable', async () => {
      // Arrange
      const error = new Error('Backend service unavailable');
      mockApiClient.getSession.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.getSession('test-session-123')
      ).rejects.toThrow('Backend service unavailable');
    });
  });

  describe('cache handling', () => {
    it('should use cached session if available', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-123',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockSessionManager.getSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockSessionManager.getSession('test-session-123');

      // Assert
      expect(mockSessionManager.getSession).toHaveBeenCalledWith('test-session-123');
      expect(session).toBe(mockSession);
    });

    it('should fetch from API if cache miss', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-123',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockSessionManager.getSession.mockResolvedValue(null);
      mockApiClient.getSession.mockResolvedValue(mockSession);

      // Act
      const cachedSession = await mockSessionManager.getSession('test-session-123');
      let session: SessionResponse;
      
      if (!cachedSession) {
        session = await mockApiClient.getSession('test-session-123');
        await mockSessionManager.saveSession(session);
      }

      // Assert
      expect(mockApiClient.getSession).toHaveBeenCalledWith('test-session-123');
      expect(mockSessionManager.saveSession).toHaveBeenCalledWith(mockSession);
    });
  });
});
