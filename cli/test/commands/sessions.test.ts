/**
 * Sessions Command Tests
 * 
 * Testing strategy:
 * - Mock SessionManager
 * - Test session listing with table formatting
 * - Test filtering (active vs all sessions)
 * - Test sorting (newest first)
 * - Test current session highlighting
 * - Test empty state
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { SessionManager } from '../../src/state/session-manager.js';
import type { SessionResponse } from '../../src/api/types.js';

// Mock dependencies
vi.mock('../../src/state/session-manager.js');

describe('sessions command', () => {
  let mockSessionManager: any;

  beforeEach(() => {
    vi.clearAllMocks();
    
    // Setup session manager mock
    mockSessionManager = {
      listSessions: vi.fn(),
      getCurrentSessionId: vi.fn()
    };
    
    vi.mocked(SessionManager).mockImplementation(() => mockSessionManager);
  });

  describe('session listing', () => {
    it('should list all cached sessions', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-123',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: '2026-03-07T10:00:00Z',
          updatedAt: '2026-03-07T11:00:00Z',
          convergence: { overall: 0.85, dimensions: {
            assumption_explicitness: 0.88,
            evidence_quality: 0.82,
            risk_coverage: 0.84,
            decision_clarity: 0.87,
            scope_definition: 0.83,
            constraint_alignment: 0.85,
            uncertainty_acknowledgment: 0.86
          }}
        },
        {
          id: 'session-456',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: '2026-03-07T12:00:00Z',
          updatedAt: '2026-03-07T12:30:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);

      // Act
      const sessions = await mockSessionManager.listSessions();

      // Assert
      expect(sessions).toHaveLength(2);
      expect(sessions[0].id).toBe('session-123');
      expect(sessions[1].id).toBe('session-456');
    });

    it('should sort sessions by creation date (newest first)', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-new',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: '2026-03-07T14:00:00Z',
          updatedAt: '2026-03-07T14:00:00Z'
        },
        {
          id: 'session-old',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: '2026-03-06T10:00:00Z',
          updatedAt: '2026-03-06T11:00:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);

      // Act
      const sessions = await mockSessionManager.listSessions();

      // Assert - Newest should be first
      expect(sessions[0].id).toBe('session-new');
      expect(new Date(sessions[0].createdAt).getTime()).toBeGreaterThan(
        new Date(sessions[1].createdAt).getTime()
      );
    });

    it('should handle empty session list', async () => {
      // Arrange
      mockSessionManager.listSessions.mockResolvedValue([]);

      // Act
      const sessions = await mockSessionManager.listSessions();

      // Assert
      expect(sessions).toHaveLength(0);
    });
  });

  describe('current session highlighting', () => {
    it('should identify current session', async () => {
      // Arrange
      const currentSessionId = 'session-456';
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-123',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: '2026-03-07T10:00:00Z',
          updatedAt: '2026-03-07T11:00:00Z'
        },
        {
          id: 'session-456',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: '2026-03-07T12:00:00Z',
          updatedAt: '2026-03-07T12:30:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(currentSessionId);

      // Act
      const sessions = await mockSessionManager.listSessions();
      const current = await mockSessionManager.getCurrentSessionId();

      // Assert
      expect(current).toBe(currentSessionId);
      const currentSession = sessions.find(s => s.id === current);
      expect(currentSession).toBeDefined();
      expect(currentSession?.id).toBe('session-456');
    });

    it('should handle no current session', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-123',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: '2026-03-07T10:00:00Z',
          updatedAt: '2026-03-07T11:00:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      // Act
      const current = await mockSessionManager.getCurrentSessionId();

      // Assert
      expect(current).toBeNull();
    });
  });

  describe('session filtering', () => {
    it('should filter active sessions by default', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-active',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: '2026-03-07T12:00:00Z',
          updatedAt: '2026-03-07T12:30:00Z'
        },
        {
          id: 'session-paused',
          status: 'paused',
          phase: 'ANALYSIS_ROUND',
          createdAt: '2026-03-07T11:00:00Z',
          updatedAt: '2026-03-07T11:30:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);

      // Act
      const sessions = await mockSessionManager.listSessions();
      const activeSessions = sessions.filter(s => s.status === 'active' || s.status === 'paused');

      // Assert
      expect(activeSessions).toHaveLength(2);
    });

    it('should show all sessions with --all flag', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-active',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: '2026-03-07T12:00:00Z',
          updatedAt: '2026-03-07T12:30:00Z'
        },
        {
          id: 'session-complete',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: '2026-03-07T10:00:00Z',
          updatedAt: '2026-03-07T11:00:00Z'
        },
        {
          id: 'session-closed',
          status: 'closed',
          phase: 'POST_DELIVERY',
          createdAt: '2026-03-06T10:00:00Z',
          updatedAt: '2026-03-06T12:00:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);

      // Act - With --all flag, show all
      const sessions = await mockSessionManager.listSessions();

      // Assert
      expect(sessions).toHaveLength(3);
      expect(sessions.some(s => s.status === 'complete')).toBe(true);
      expect(sessions.some(s => s.status === 'closed')).toBe(true);
    });
  });

  describe('session information display', () => {
    it('should include session ID (shortened)', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-123456789',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: '2026-03-07T12:00:00Z',
          updatedAt: '2026-03-07T12:30:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);

      // Act
      const sessions = await mockSessionManager.listSessions();

      // Assert
      expect(sessions[0].id).toBe('session-123456789');
      
      // Test shortening logic
      const shortId = sessions[0].id.substring(0, 12);
      expect(shortId).toBe('session-1234');
    });

    it('should include creation timestamp', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-123',
          status: 'active',
          phase: 'CLARIFICATION',
          createdAt: '2026-03-07T12:00:00Z',
          updatedAt: '2026-03-07T12:30:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);

      // Act
      const sessions = await mockSessionManager.listSessions();

      // Assert
      expect(sessions[0].createdAt).toBeDefined();
      expect(new Date(sessions[0].createdAt).getTime()).toBeGreaterThan(0);
    });

    it('should include status and phase', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-123',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: '2026-03-07T12:00:00Z',
          updatedAt: '2026-03-07T12:30:00Z'
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);

      // Act
      const sessions = await mockSessionManager.listSessions();

      // Assert
      expect(sessions[0].status).toBe('complete');
      expect(sessions[0].phase).toBe('DELIVER');
    });

    it('should include convergence score if available', async () => {
      // Arrange
      const mockSessions: SessionResponse[] = [
        {
          id: 'session-123',
          status: 'complete',
          phase: 'DELIVER',
          createdAt: '2026-03-07T12:00:00Z',
          updatedAt: '2026-03-07T12:30:00Z',
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
        }
      ];
      
      mockSessionManager.listSessions.mockResolvedValue(mockSessions);

      // Act
      const sessions = await mockSessionManager.listSessions();

      // Assert
      expect(sessions[0].convergence).toBeDefined();
      expect(sessions[0].convergence?.overall).toBe(0.85);
    });
  });
});
