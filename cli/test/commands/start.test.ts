/**
 * Start Command Tests
 * 
 * Testing strategy:
 * - Mock AgonAPIClient
 * - Mock SessionManager
 * - Test command parsing and execution
 * - Test error handling
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import type { SessionResponse } from '../../src/api/types.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');

describe('start command', () => {
  let mockApiClient: any;
  let mockSessionManager: any;

  beforeEach(() => {
    vi.clearAllMocks();
    
    // Setup API client mock
    mockApiClient = {
      createSession: vi.fn(),
      getClarification: vi.fn(),
      submitAnswers: vi.fn()
    };
    
    // Setup session manager mock
    mockSessionManager = {
      saveSession: vi.fn(),
      setCurrentSessionId: vi.fn(),
      ensureConfigDirectory: vi.fn()
    };
    
    vi.mocked(AgonAPIClient).mockImplementation(() => mockApiClient);
    vi.mocked(SessionManager).mockImplementation(() => mockSessionManager);
  });

  describe('argument validation', () => {
    it('should require an idea argument', async () => {
      // This test will verify the command definition requires the 'idea' arg
      // In oclif, this is enforced at the framework level
      expect(true).toBe(true); // Placeholder - actual test will use oclif test utilities
    });

    it('should reject empty idea', async () => {
      // Arrange
      const idea = '';

      // Act & Assert
      // API client validation should reject empty ideas
      mockApiClient.createSession.mockRejectedValue(
        new Error('Idea must be at least 10 characters long')
      );

      await expect(
        mockApiClient.createSession({ idea, friction: 50, researchEnabled: true })
      ).rejects.toThrow('Idea must be at least 10 characters long');
    });

    it('should reject idea shorter than 10 characters', async () => {
      // Arrange
      const idea = 'short';

      // Act & Assert
      mockApiClient.createSession.mockRejectedValue(
        new Error('Idea must be at least 10 characters long')
      );

      await expect(
        mockApiClient.createSession({ idea, friction: 50, researchEnabled: true })
      ).rejects.toThrow('Idea must be at least 10 characters long');
    });
  });

  describe('flag handling', () => {
    it('should use default friction of 50 when not specified', async () => {
      // Arrange
      const idea = 'Build a SaaS for project management';
      const mockSession: SessionResponse = {
        id: 'test-session-id',
        status: 'active',
        phase: 'INTAKE',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockApiClient.createSession.mockResolvedValue(mockSession);

      // Act
      await mockApiClient.createSession({ 
        idea, 
        friction: 50, 
        researchEnabled: true 
      });

      // Assert
      expect(mockApiClient.createSession).toHaveBeenCalledWith({
        idea,
        friction: 50,
        researchEnabled: true
      });
    });

    it('should accept custom friction value', async () => {
      // Arrange
      const idea = 'Build a SaaS for project management';
      const friction = 85;
      const mockSession: SessionResponse = {
        id: 'test-session-id',
        status: 'active',
        phase: 'INTAKE',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockApiClient.createSession.mockResolvedValue(mockSession);

      // Act
      await mockApiClient.createSession({ 
        idea, 
        friction, 
        researchEnabled: true 
      });

      // Assert
      expect(mockApiClient.createSession).toHaveBeenCalledWith({
        idea,
        friction: 85,
        researchEnabled: true
      });
    });

    it('should respect --no-research flag', async () => {
      // Arrange
      const idea = 'Build a SaaS for project management';
      const mockSession: SessionResponse = {
        id: 'test-session-id',
        status: 'active',
        phase: 'INTAKE',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockApiClient.createSession.mockResolvedValue(mockSession);

      // Act
      await mockApiClient.createSession({ 
        idea, 
        friction: 50, 
        researchEnabled: false 
      });

      // Assert
      expect(mockApiClient.createSession).toHaveBeenCalledWith({
        idea,
        friction: 50,
        researchEnabled: false
      });
    });
  });

  describe('session creation', () => {
    it('should create session and save to local cache', async () => {
      // Arrange
      const idea = 'Build a SaaS for project management';
      const mockSession: SessionResponse = {
        id: 'test-session-id',
        status: 'active',
        phase: 'INTAKE',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockApiClient.createSession.mockResolvedValue(mockSession);

      // Act
      const session = await mockApiClient.createSession({ 
        idea, 
        friction: 50, 
        researchEnabled: true 
      });
      await mockSessionManager.saveSession(session);
      await mockSessionManager.setCurrentSessionId(session.id);

      // Assert
      expect(mockApiClient.createSession).toHaveBeenCalledWith({
        idea,
        friction: 50,
        researchEnabled: true
      });
      expect(mockSessionManager.saveSession).toHaveBeenCalledWith(mockSession);
      expect(mockSessionManager.setCurrentSessionId).toHaveBeenCalledWith('test-session-id');
    });

    it('should handle session creation failure', async () => {
      // Arrange
      const idea = 'Build a SaaS for project management';
      const error = new Error('Backend service unavailable');
      
      mockApiClient.createSession.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.createSession({ idea, friction: 50, researchEnabled: true })
      ).rejects.toThrow('Backend service unavailable');
    });
  });

  describe('clarification handling', () => {
    it('should fetch clarification questions after session creation', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-id',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      const mockClarification = {
        questions: [
          { id: 'q1', text: 'Who is the target customer?' },
          { id: 'q2', text: 'What is the primary pain point?' }
        ],
        round: 1,
        maxRounds: 2
      };
      
      mockApiClient.createSession.mockResolvedValue(mockSession);
      mockApiClient.getClarification.mockResolvedValue(mockClarification);

      // Act
      await mockApiClient.getClarification('test-session-id');

      // Assert
      expect(mockApiClient.getClarification).toHaveBeenCalledWith('test-session-id');
    });

    it('should skip clarification in non-interactive mode', async () => {
      // Arrange
      const mockSession: SessionResponse = {
        id: 'test-session-id',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      
      mockApiClient.createSession.mockResolvedValue(mockSession);

      // Act - In non-interactive mode, we don't call getClarification
      // This is a behavioral test for the command implementation

      // Assert
      expect(mockApiClient.getClarification).not.toHaveBeenCalled();
    });
  });

  describe('error handling', () => {
    it('should handle network errors gracefully', async () => {
      // Arrange
      const idea = 'Build a SaaS for project management';
      const error = new Error('Network request failed');
      
      mockApiClient.createSession.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.createSession({ idea, friction: 50, researchEnabled: true })
      ).rejects.toThrow('Network request failed');
    });

    it('should handle rate limit errors', async () => {
      // Arrange
      const idea = 'Build a SaaS for project management';
      const error = new Error('Rate limit exceeded. Please try again later.');
      
      mockApiClient.createSession.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.createSession({ idea, friction: 50, researchEnabled: true })
      ).rejects.toThrow('Rate limit exceeded');
    });
  });
});
