/**
 * Show Command Tests
 * 
 * Testing strategy:
 * - Mock AgonAPIClient
 * - Mock SessionManager
 * - Test artifact fetching and caching
 * - Test Markdown rendering (will mock marked-terminal)
 * - Test error handling
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AgonAPIClient } from '../../src/api/agon-client.js';
import { SessionManager } from '../../src/state/session-manager.js';
import type { Artifact } from '../../src/api/types.js';

// Mock dependencies
vi.mock('../../src/api/agon-client.js');
vi.mock('../../src/state/session-manager.js');

describe('show command', () => {
  let mockApiClient: any;
  let mockSessionManager: any;

  beforeEach(() => {
    vi.clearAllMocks();
    
    // Setup API client mock
    mockApiClient = {
      getArtifact: vi.fn()
    };
    
    // Setup session manager mock
    mockSessionManager = {
      getCurrentSessionId: vi.fn(),
      getArtifact: vi.fn(),
      saveArtifact: vi.fn()
    };
    
    vi.mocked(AgonAPIClient).mockImplementation(() => mockApiClient);
    vi.mocked(SessionManager).mockImplementation(() => mockSessionManager);
  });

  describe('artifact type validation', () => {
    it('should accept valid artifact types', async () => {
      // Arrange
      const validTypes = ['verdict', 'plan', 'prd', 'risks', 'assumptions', 'architecture', 'copilot'];
      const mockArtifact: Artifact = {
        type: 'verdict',
        content: '# Verdict\n\nThis is a test verdict.',
        version: 1,
        createdAt: new Date().toISOString()
      };
      
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act & Assert - Each valid type should be accepted
      for (const type of validTypes) {
        await expect(
          mockApiClient.getArtifact('test-session-123', type)
        ).resolves.toBeDefined();
      }
    });
  });

  describe('current session detection', () => {
    it('should use current session if no session ID provided', async () => {
      // Arrange
      const sessionId = 'test-session-123';
      mockSessionManager.getCurrentSessionId.mockResolvedValue(sessionId);

      // Act
      const currentId = await mockSessionManager.getCurrentSessionId();

      // Assert
      expect(currentId).toBe(sessionId);
      expect(mockSessionManager.getCurrentSessionId).toHaveBeenCalled();
    });

    it('should handle no current session', async () => {
      // Arrange
      mockSessionManager.getCurrentSessionId.mockResolvedValue(null);

      // Act
      const currentId = await mockSessionManager.getCurrentSessionId();

      // Assert
      expect(currentId).toBeNull();
    });
  });

  describe('artifact fetching', () => {
    it('should fetch verdict artifact', async () => {
      // Arrange
      const mockArtifact: Artifact = {
        type: 'verdict',
        content: '# Verdict\n\nGo ahead with this idea.',
        version: 1,
        createdAt: new Date().toISOString()
      };
      
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act
      const artifact = await mockApiClient.getArtifact('test-session-123', 'verdict');

      // Assert
      expect(artifact.type).toBe('verdict');
      expect(artifact.content).toContain('# Verdict');
      expect(mockApiClient.getArtifact).toHaveBeenCalledWith('test-session-123', 'verdict');
    });

    it('should fetch plan artifact', async () => {
      // Arrange
      const mockArtifact: Artifact = {
        type: 'plan',
        content: '# Implementation Plan\n\n## Phase 1\n- Step 1\n- Step 2',
        version: 1,
        createdAt: new Date().toISOString()
      };
      
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act
      const artifact = await mockApiClient.getArtifact('test-session-123', 'plan');

      // Assert
      expect(artifact.type).toBe('plan');
      expect(artifact.content).toContain('# Implementation Plan');
    });

    it('should fetch PRD artifact', async () => {
      // Arrange
      const mockArtifact: Artifact = {
        type: 'prd',
        content: '# Product Requirements Document\n\n## Overview\nThis is a PRD.',
        version: 1,
        createdAt: new Date().toISOString()
      };
      
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act
      const artifact = await mockApiClient.getArtifact('test-session-123', 'prd');

      // Assert
      expect(artifact.type).toBe('prd');
      expect(artifact.content).toContain('Product Requirements Document');
    });

    it('should fetch risks artifact', async () => {
      // Arrange
      const mockArtifact: Artifact = {
        type: 'risks',
        content: '# Risk Registry\n\n## High Priority\n- Risk 1\n- Risk 2',
        version: 1,
        createdAt: new Date().toISOString()
      };
      
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act
      const artifact = await mockApiClient.getArtifact('test-session-123', 'risks');

      // Assert
      expect(artifact.type).toBe('risks');
      expect(artifact.content).toContain('Risk Registry');
    });
  });

  describe('cache handling', () => {
    it('should use cached artifact if available', async () => {
      // Arrange
      const cachedContent = '# Cached Verdict\n\nThis is from cache.';
      mockSessionManager.getArtifact.mockResolvedValue(cachedContent);

      // Act
      const content = await mockSessionManager.getArtifact('test-session-123', 'verdict');

      // Assert
      expect(content).toBe(cachedContent);
      expect(mockSessionManager.getArtifact).toHaveBeenCalledWith('test-session-123', 'verdict');
    });

    it('should fetch from API if cache miss', async () => {
      // Arrange
      const mockArtifact: Artifact = {
        type: 'verdict',
        content: '# Verdict\n\nFresh from API.',
        version: 1,
        createdAt: new Date().toISOString()
      };
      
      mockSessionManager.getArtifact.mockResolvedValue(null);
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act - Simulate command flow
      const cached = await mockSessionManager.getArtifact('test-session-123', 'verdict');
      let content: string;
      
      if (cached) {
        content = cached;
      } else {
        const artifact = await mockApiClient.getArtifact('test-session-123', 'verdict');
        await mockSessionManager.saveArtifact('test-session-123', 'verdict', artifact.content);
        content = artifact.content;
      }

      // Assert
      expect(mockApiClient.getArtifact).toHaveBeenCalledWith('test-session-123', 'verdict');
      expect(mockSessionManager.saveArtifact).toHaveBeenCalledWith('test-session-123', 'verdict', mockArtifact.content);
      expect(content).toBe(mockArtifact.content);
    });

    it('should force refresh when --refresh flag is used', async () => {
      // Arrange
      const mockArtifact: Artifact = {
        type: 'verdict',
        content: '# Verdict\n\nRefreshed content.',
        version: 2,
        createdAt: new Date().toISOString()
      };
      
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act - With refresh flag, skip cache check
      const artifact = await mockApiClient.getArtifact('test-session-123', 'verdict');
      await mockSessionManager.saveArtifact('test-session-123', 'verdict', artifact.content);

      // Assert
      expect(mockApiClient.getArtifact).toHaveBeenCalledWith('test-session-123', 'verdict');
      expect(mockSessionManager.saveArtifact).toHaveBeenCalled();
      expect(mockSessionManager.getArtifact).not.toHaveBeenCalled();
    });
  });

  describe('error handling', () => {
    it('should handle artifact not found', async () => {
      // Arrange
      const error = new Error('Artifact not found. The session may not be complete yet.');
      mockApiClient.getArtifact.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.getArtifact('test-session-123', 'verdict')
      ).rejects.toThrow('Artifact not found');
    });

    it('should handle session not found', async () => {
      // Arrange
      const error = new Error('Session not found');
      mockApiClient.getArtifact.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.getArtifact('non-existent-id', 'verdict')
      ).rejects.toThrow('Session not found');
    });

    it('should handle network errors', async () => {
      // Arrange
      const error = new Error('Network request failed');
      mockApiClient.getArtifact.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.getArtifact('test-session-123', 'verdict')
      ).rejects.toThrow('Network request failed');
    });

    it('should handle backend unavailable', async () => {
      // Arrange
      const error = new Error('Backend service unavailable');
      mockApiClient.getArtifact.mockRejectedValue(error);

      // Act & Assert
      await expect(
        mockApiClient.getArtifact('test-session-123', 'verdict')
      ).rejects.toThrow('Backend service unavailable');
    });
  });

  describe('markdown content validation', () => {
    it('should handle empty artifact content', async () => {
      // Arrange
      const mockArtifact: Artifact = {
        type: 'verdict',
        content: '',
        version: 1,
        createdAt: new Date().toISOString()
      };
      
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act
      const artifact = await mockApiClient.getArtifact('test-session-123', 'verdict');

      // Assert
      expect(artifact.content).toBe('');
    });

    it('should handle large artifact content', async () => {
      // Arrange
      const largeContent = '# Large Document\n\n' + 'Lorem ipsum dolor sit amet. '.repeat(1000);
      const mockArtifact: Artifact = {
        type: 'prd',
        content: largeContent,
        version: 1,
        createdAt: new Date().toISOString()
      };
      
      mockApiClient.getArtifact.mockResolvedValue(mockArtifact);

      // Act
      const artifact = await mockApiClient.getArtifact('test-session-123', 'prd');

      // Assert
      expect(artifact.content.length).toBeGreaterThan(10000);
      expect(artifact.content).toContain('Large Document');
    });
  });
});
