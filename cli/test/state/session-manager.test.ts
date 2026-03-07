/**
 * SessionManager Tests
 * 
 * Testing strategy:
 * - Mock filesystem operations
 * - Test session caching and retrieval
 * - Test current session tracking
 * - Test artifact storage
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { SessionManager } from '../../src/state/session-manager.js';
import type { SessionResponse } from '../../src/api/types.js';
import * as fs from 'fs/promises';
import * as path from 'path';
import os from 'os';

// Mock fs module
vi.mock('fs/promises');

describe('SessionManager', () => {
  let sessionManager: SessionManager;
  const testConfigDir = path.join(os.tmpdir(), '.agon-test');

  beforeEach(() => {
    vi.clearAllMocks();
    sessionManager = new SessionManager(testConfigDir);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('initialization', () => {
    it('should create config directory on first use', async () => {
      // Arrange
      (fs.mkdir as any).mockResolvedValue(undefined);
      (fs.access as any).mockRejectedValue(new Error('Directory does not exist'));

      // Act
      await sessionManager.ensureConfigDirectory();

      // Assert
      expect(fs.mkdir).toHaveBeenCalledWith(
        expect.stringContaining('.agon-test'),
        { recursive: true }
      );
    });

    it('should not recreate directory if it exists', async () => {
      // Arrange
      (fs.access as any).mockResolvedValue(undefined);

      // Act
      await sessionManager.ensureConfigDirectory();

      // Assert
      expect(fs.mkdir).not.toHaveBeenCalled();
    });
  });

  describe('saveSession', () => {
    it('should save session to cache file', async () => {
      // Arrange
      const session: SessionResponse = {
        id: 'test-123',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T10:00:00Z'
      };

      (fs.access as any).mockResolvedValue(undefined);
      (fs.mkdir as any).mockResolvedValue(undefined);
      (fs.writeFile as any).mockResolvedValue(undefined);

      // Act
      await sessionManager.saveSession(session);

      // Assert
      expect(fs.writeFile).toHaveBeenCalledWith(
        expect.stringContaining('test-123.json'),
        expect.stringContaining('"id": "test-123"'),
        'utf-8'
      );
    });
  });

  describe('getSession', () => {
    it('should retrieve cached session', async () => {
      // Arrange
      const session: SessionResponse = {
        id: 'test-123',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T10:00:00Z'
      };

      (fs.access as any).mockResolvedValue(undefined);
      (fs.readFile as any).mockResolvedValue(JSON.stringify(session));

      // Act
      const result = await sessionManager.getSession('test-123');

      // Assert
      expect(result).toEqual(session);
      expect(fs.readFile).toHaveBeenCalledWith(
        expect.stringContaining('test-123.json'),
        'utf-8'
      );
    });

    it('should return null if session not found', async () => {
      // Arrange
      (fs.access as any).mockResolvedValue(undefined);
      (fs.readFile as any).mockRejectedValue(new Error('File not found'));

      // Act
      const result = await sessionManager.getSession('non-existent');

      // Assert
      expect(result).toBeNull();
    });
  });

  describe('getCurrentSessionId', () => {
    it('should return current session ID from file', async () => {
      // Arrange
      (fs.access as any).mockResolvedValue(undefined);
      (fs.readFile as any).mockResolvedValue('test-123');

      // Act
      const result = await sessionManager.getCurrentSessionId();

      // Assert
      expect(result).toBe('test-123');
      expect(fs.readFile).toHaveBeenCalledWith(
        expect.stringContaining('current-session'),
        'utf-8'
      );
    });

    it('should return null if no current session', async () => {
      // Arrange
      (fs.access as any).mockResolvedValue(undefined);
      (fs.readFile as any).mockRejectedValue(new Error('File not found'));

      // Act
      const result = await sessionManager.getCurrentSessionId();

      // Assert
      expect(result).toBeNull();
    });
  });

  describe('setCurrentSessionId', () => {
    it('should write session ID to current-session file', async () => {
      // Arrange
      (fs.access as any).mockResolvedValue(undefined);
      (fs.writeFile as any).mockResolvedValue(undefined);

      // Act
      await sessionManager.setCurrentSessionId('test-123');

      // Assert
      expect(fs.writeFile).toHaveBeenCalledWith(
        expect.stringContaining('current-session'),
        'test-123',
        'utf-8'
      );
    });
  });

  describe('listSessions', () => {
    it('should return all cached sessions', async () => {
      // Arrange
      const session1: SessionResponse = {
        id: 'test-1',
        status: 'active',
        phase: 'CLARIFICATION',
        createdAt: '2026-03-07T10:00:00Z',
        updatedAt: '2026-03-07T10:00:00Z'
      };

      const session2: SessionResponse = {
        id: 'test-2',
        status: 'complete',
        phase: 'DELIVER',
        createdAt: '2026-03-07T09:00:00Z',
        updatedAt: '2026-03-07T09:30:00Z'
      };

      (fs.access as any).mockResolvedValue(undefined);
      (fs.readdir as any).mockResolvedValue(['test-1.json', 'test-2.json']);
      (fs.readFile as any)
        .mockResolvedValueOnce(JSON.stringify(session1))
        .mockResolvedValueOnce(JSON.stringify(session2));

      // Act
      const result = await sessionManager.listSessions();

      // Assert
      expect(result).toHaveLength(2);
      expect(result[0].id).toBe('test-1');
      expect(result[1].id).toBe('test-2');
    });

    it('should return empty array if no sessions', async () => {
      // Arrange
      (fs.access as any).mockResolvedValue(undefined);
      (fs.readdir as any).mockResolvedValue([]);

      // Act
      const result = await sessionManager.listSessions();

      // Assert
      expect(result).toEqual([]);
    });
  });

  describe('saveArtifact', () => {
    it('should save artifact to session directory', async () => {
      // Arrange
      const sessionId = 'test-123';
      const artifactType = 'verdict';
      const content = '# Verdict\n\nThis is a test verdict.';

      (fs.access as any).mockResolvedValue(undefined);
      (fs.mkdir as any).mockResolvedValue(undefined);
      (fs.writeFile as any).mockResolvedValue(undefined);

      // Act
      await sessionManager.saveArtifact(sessionId, artifactType, content);

      // Assert
      expect(fs.mkdir).toHaveBeenCalledWith(
        expect.stringContaining('test-123'),
        { recursive: true }
      );
      expect(fs.writeFile).toHaveBeenCalledWith(
        expect.stringContaining('verdict.md'),
        content,
        'utf-8'
      );
    });
  });

  describe('getArtifact', () => {
    it('should retrieve cached artifact', async () => {
      // Arrange
      const content = '# Verdict\n\nThis is a test verdict.';
      (fs.access as any).mockResolvedValue(undefined);
      (fs.readFile as any).mockResolvedValue(content);

      // Act
      const result = await sessionManager.getArtifact('test-123', 'verdict');

      // Assert
      expect(result).toBe(content);
      expect(fs.readFile).toHaveBeenCalledWith(
        expect.stringContaining('verdict.md'),
        'utf-8'
      );
    });

    it('should return null if artifact not found', async () => {
      // Arrange
      (fs.access as any).mockResolvedValue(undefined);
      (fs.readFile as any).mockRejectedValue(new Error('File not found'));

      // Act
      const result = await sessionManager.getArtifact('test-123', 'verdict');

      // Assert
      expect(result).toBeNull();
    });
  });

  describe('clearSession', () => {
    it('should remove session cache file', async () => {
      // Arrange
      (fs.access as any).mockResolvedValue(undefined);
      (fs.unlink as any).mockResolvedValue(undefined);

      // Act
      await sessionManager.clearSession('test-123');

      // Assert
      expect(fs.unlink).toHaveBeenCalledWith(
        expect.stringContaining('test-123.json')
      );
    });
  });
});
