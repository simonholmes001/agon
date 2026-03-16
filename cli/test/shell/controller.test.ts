import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Message, SessionResponse } from '../../src/api/types.js';
import { ShellController } from '../../src/shell/controller.js';

describe('shell controller', () => {
  let apiClient: any;
  let sessionManager: any;
  let configManager: any;
  let controller: ShellController;

  const baseSession: SessionResponse = {
    id: 'session-123',
    status: 'active',
    phase: 'Clarification',
    createdAt: '2026-03-10T10:00:00Z',
    updatedAt: '2026-03-10T10:00:00Z'
  };

  beforeEach(() => {
    apiClient = {
      createSession: vi.fn(),
      startSession: vi.fn(),
      getSession: vi.fn(),
      listSessions: vi.fn(),
      getMessages: vi.fn(),
      submitMessage: vi.fn(),
      getArtifact: vi.fn(),
      uploadAttachment: vi.fn()
    };

    sessionManager = {
      saveSession: vi.fn(),
      setCurrentSessionId: vi.fn(),
      getCurrentSessionId: vi.fn(),
      listSessions: vi.fn(),
      getSession: vi.fn(),
      getArtifact: vi.fn(),
      saveArtifact: vi.fn()
    };

    configManager = {
      load: vi.fn().mockResolvedValue({
        apiUrl: 'http://localhost:5000',
        defaultFriction: 50,
        researchEnabled: true,
        logLevel: 'info'
      }),
      set: vi.fn()
    };

    controller = new ShellController({
      apiClient,
      sessionManager,
      configManager,
      followUpPollIntervalMs: 1,
      followUpTimeoutMs: 5
    });
  });

  it('starts idea using persisted default friction and research settings', async () => {
    const moderatorMessage: Message = {
      agentId: 'moderator',
      message: 'Who is the target user?',
      round: 0,
      createdAt: '2026-03-10T10:00:30Z'
    };

    apiClient.createSession.mockResolvedValue(baseSession);
    apiClient.getSession.mockResolvedValue(baseSession);
    apiClient.getMessages.mockResolvedValue([moderatorMessage]);

    const result = await controller.startIdea('Build an iOS app');

    expect(apiClient.createSession).toHaveBeenCalledWith({
      idea: 'Build an iOS app',
      friction: 50,
      researchEnabled: true
    });
    expect(apiClient.startSession).toHaveBeenCalledWith('session-123');
    expect(apiClient.getMessages).toHaveBeenCalledWith('session-123');
    expect(sessionManager.saveSession).toHaveBeenCalledWith(baseSession);
    expect(sessionManager.setCurrentSessionId).toHaveBeenCalledWith('session-123');
    expect(result.session.id).toBe('session-123');
    expect(result.responseMessage?.message).toBe('Who is the target user?');
  });

  it('submits follow-up and returns new post-delivery assistant message', async () => {
    const postSession: SessionResponse = { ...baseSession, phase: 'PostDelivery' };
    const oldMessage: Message = {
      agentId: 'post_delivery_assistant',
      message: 'Old',
      round: 2,
      createdAt: '2026-03-10T10:00:00Z'
    };
    const newMessage: Message = {
      agentId: 'post_delivery_assistant',
      message: 'Revised PRD',
      round: 2,
      createdAt: '2026-03-10T10:01:00Z'
    };

    sessionManager.getCurrentSessionId.mockResolvedValue('session-123');
    apiClient.getMessages
      .mockResolvedValueOnce([oldMessage])
      .mockResolvedValueOnce([oldMessage, newMessage]);
    apiClient.submitMessage.mockResolvedValue(postSession);

    const result = await controller.submitFollowUp('Revise section 2');

    expect(apiClient.submitMessage).toHaveBeenCalledWith('session-123', 'Revise section 2');
    expect(result.responseMessage?.message).toBe('Revised PRD');
  });

  it('waits for a new moderator response in clarification follow-up flow', async () => {
    const oldModerator: Message = {
      agentId: 'moderator',
      message: 'Old clarification question',
      round: 0,
      createdAt: '2026-03-10T10:00:00Z'
    };
    const newModerator: Message = {
      agentId: 'moderator',
      message: 'New clarification question',
      round: 1,
      createdAt: '2026-03-10T10:01:00Z'
    };

    sessionManager.getCurrentSessionId.mockResolvedValue('session-123');
    apiClient.submitMessage.mockResolvedValue(baseSession);
    apiClient.getSession.mockResolvedValue(baseSession);
    apiClient.getMessages
      .mockResolvedValueOnce([oldModerator]) // before submit
      .mockResolvedValueOnce([oldModerator]) // immediate after submit (still old)
      .mockResolvedValueOnce([oldModerator, newModerator]); // polled follow-up

    const result = await controller.submitFollowUp('Answer to clarification');

    expect(apiClient.submitMessage).toHaveBeenCalledWith('session-123', 'Answer to clarification');
    expect(result.responseMessage?.message).toBe('New clarification question');
  });

  it('returns new agent output when clarification follow-up transitions into debate phases', async () => {
    const oldModerator: Message = {
      agentId: 'moderator',
      message: 'Old clarification question',
      round: 0,
      createdAt: '2026-03-10T10:00:00Z'
    };
    const debateAgentMessage: Message = {
      agentId: 'gpt_agent',
      message: 'Initial analysis from debate',
      round: 0,
      createdAt: '2026-03-10T10:01:00Z'
    };

    const clarificationSession: SessionResponse = { ...baseSession, phase: 'Clarification' };
    const analysisSession: SessionResponse = { ...baseSession, phase: 'AnalysisRound' };

    sessionManager.getCurrentSessionId.mockResolvedValue('session-123');
    apiClient.submitMessage.mockResolvedValue(clarificationSession);
    apiClient.getSession.mockResolvedValue(analysisSession);
    apiClient.getMessages
      .mockResolvedValueOnce([oldModerator]) // before submit
      .mockResolvedValueOnce([oldModerator]) // immediate after submit
      .mockResolvedValueOnce([oldModerator, debateAgentMessage]); // polled after phase transition

    const result = await controller.submitFollowUp('Answer to clarification');

    expect(apiClient.submitMessage).toHaveBeenCalledWith('session-123', 'Answer to clarification');
    expect(result.session.phase).toBe('AnalysisRound');
    expect(result.responseMessage?.agentId).toBe('gpt_agent');
    expect(result.responseMessage?.message).toBe('Initial analysis from debate');
  });

  it('sets parameter values with key-specific parsing', async () => {
    await controller.setParam('defaultFriction', '75');
    await controller.setParam('researchEnabled', 'false');

    expect(configManager.set).toHaveBeenNthCalledWith(1, 'defaultFriction', 75);
    expect(configManager.set).toHaveBeenNthCalledWith(2, 'researchEnabled', false);
  });

  it('selects a session by validating it from API when not cached', async () => {
    sessionManager.getSession.mockResolvedValue(null);
    apiClient.getSession.mockResolvedValue(baseSession);

    const session = await controller.selectSession('session-123');

    expect(apiClient.getSession).toHaveBeenCalledWith('session-123');
    expect(sessionManager.setCurrentSessionId).toHaveBeenCalledWith('session-123');
    expect(session.id).toBe('session-123');
  });

  it('fetches status using explicit session or current session', async () => {
    sessionManager.getCurrentSessionId.mockResolvedValue('session-123');
    apiClient.getSession.mockResolvedValue(baseSession);

    const session = await controller.getStatus();

    expect(apiClient.getSession).toHaveBeenCalledWith('session-123');
    expect(session.id).toBe('session-123');
  });

  it('lists sessions from API and syncs cache', async () => {
    const sessions: SessionResponse[] = [
      { ...baseSession, id: 'older', updatedAt: '2026-03-10T10:00:00Z' },
      { ...baseSession, id: 'newer', updatedAt: '2026-03-10T11:00:00Z' }
    ];
    apiClient.listSessions.mockResolvedValue(sessions);

    const result = await controller.listSessions();

    expect(apiClient.listSessions).toHaveBeenCalled();
    expect(sessionManager.saveSession).toHaveBeenCalledTimes(2);
    expect(result[0].id).toBe('newer');
  });

  it('resumes latest active session when session id is omitted', async () => {
    const sessions: SessionResponse[] = [
      { ...baseSession, id: 'complete-1', status: 'complete', updatedAt: '2026-03-10T10:00:00Z' },
      { ...baseSession, id: 'active-1', status: 'active', updatedAt: '2026-03-10T09:00:00Z' }
    ];
    apiClient.listSessions.mockResolvedValue(sessions);
    sessionManager.getSession.mockResolvedValue({ ...baseSession, id: 'active-1' });

    const resumed = await controller.resumeSession();

    expect(resumed.id).toBe('active-1');
    expect(sessionManager.setCurrentSessionId).toHaveBeenCalledWith('active-1');
  });

  it('shows artifact using cache when refresh is false', async () => {
    sessionManager.getCurrentSessionId.mockResolvedValue('session-123');
    sessionManager.getArtifact.mockResolvedValue('# Cached Verdict');

    const result = await controller.getArtifact('verdict', { refresh: false, raw: false });

    expect(sessionManager.getArtifact).toHaveBeenCalledWith('session-123', 'verdict');
    expect(apiClient.getArtifact).not.toHaveBeenCalled();
    expect(result.content).toBe('# Cached Verdict');
  });

  it('uploads an attachment to the active session', async () => {
    sessionManager.getCurrentSessionId.mockResolvedValue('session-123');
    apiClient.uploadAttachment.mockResolvedValue({
      id: 'att-1',
      sessionId: 'session-123',
      fileName: 'brief.md',
      contentType: 'text/markdown',
      sizeBytes: 2048,
      accessUrl: 'https://example.test/brief.md',
      uploadedAt: '2026-03-16T12:00:00Z',
      hasExtractedText: true
    });

    const result = await controller.attachDocument('./brief.md');

    expect(apiClient.uploadAttachment).toHaveBeenCalledWith('session-123', './brief.md');
    expect(result.sessionId).toBe('session-123');
    expect(result.attachment.fileName).toBe('brief.md');
  });

  it('treats /new as awaiting-idea mode and ignores persisted current session', async () => {
    sessionManager.getCurrentSessionId.mockResolvedValue('session-123');

    await controller.clearShellSessionSelection();
    const active = await controller.getActiveSession();

    expect(active).toBeNull();
    expect(apiClient.getSession).not.toHaveBeenCalled();
  });

  it('does not auto-resume persisted session for plain-input routing until shell session is selected', async () => {
    sessionManager.getCurrentSessionId.mockResolvedValue('session-123');

    const active = await controller.getActiveSession();

    expect(active).toBeNull();
    expect(apiClient.getSession).not.toHaveBeenCalled();
  });
});
