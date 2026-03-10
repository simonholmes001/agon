import type { ArtifactType, Message, SessionResponse } from '../api/types.js';
import {
  getLatestPostDeliveryAssistantMessage,
  getLatestResponseMessageAfter,
  isPostDeliveryPhase,
  normalizeStatus
} from '../utils/session-flow.js';
import type { ShellSettableKey } from './types.js';

interface ApiClientLike {
  createSession(request: {
    idea: string;
    friction?: number;
    researchEnabled?: boolean;
  }): Promise<SessionResponse>;
  startSession(sessionId: string): Promise<void>;
  getSession(sessionId: string): Promise<SessionResponse>;
  getMessages(sessionId: string): Promise<Message[]>;
  submitMessage(sessionId: string, content: string): Promise<SessionResponse>;
  getArtifact(sessionId: string, type: ArtifactType): Promise<{ content: string }>;
}

interface SessionManagerLike {
  saveSession(session: SessionResponse): Promise<void>;
  setCurrentSessionId(sessionId: string): Promise<void>;
  getCurrentSessionId(): Promise<string | null>;
  listSessions(): Promise<SessionResponse[]>;
  getSession(sessionId: string): Promise<SessionResponse | null>;
  getArtifact(sessionId: string, type: ArtifactType): Promise<string | null>;
  saveArtifact(sessionId: string, type: ArtifactType, content: string): Promise<void>;
}

interface ConfigManagerLike {
  load(): Promise<{
    apiUrl: string;
    defaultFriction: number;
    researchEnabled: boolean;
    logLevel: 'debug' | 'info' | 'warn' | 'error';
  }>;
  set(key: ShellSettableKey, value: string | number | boolean): Promise<void>;
}

export interface ShellControllerDeps {
  apiClient: ApiClientLike;
  sessionManager: SessionManagerLike;
  configManager: ConfigManagerLike;
  followUpPollIntervalMs?: number;
  followUpTimeoutMs?: number;
}

export class ShellController {
  private readonly apiClient: ApiClientLike;
  private readonly sessionManager: SessionManagerLike;
  private readonly configManager: ConfigManagerLike;
  private readonly followUpPollIntervalMs: number;
  private readonly followUpTimeoutMs: number;
  private shellSessionId: string | null = null;
  private awaitingNewIdea = false;

  constructor(deps: ShellControllerDeps) {
    this.apiClient = deps.apiClient;
    this.sessionManager = deps.sessionManager;
    this.configManager = deps.configManager;
    this.followUpPollIntervalMs = deps.followUpPollIntervalMs ?? 1000;
    this.followUpTimeoutMs = deps.followUpTimeoutMs ?? 30000;
  }

  async getParamsSnapshot(): Promise<{
    config: Awaited<ReturnType<ConfigManagerLike['load']>>;
    session: SessionResponse | null;
  }> {
    const config = await this.configManager.load();
    const session = await this.getActiveSession();
    return { config, session };
  }

  async startIdea(
    idea: string,
    overrides?: { friction?: number; researchEnabled?: boolean }
  ): Promise<{ session: SessionResponse; responseMessage?: Message }> {
    const config = await this.configManager.load();
    const created = await this.apiClient.createSession({
      idea,
      friction: overrides?.friction ?? config.defaultFriction,
      researchEnabled: overrides?.researchEnabled ?? config.researchEnabled
    });

    await this.sessionManager.saveSession(created);
    await this.sessionManager.setCurrentSessionId(created.id);
    this.shellSessionId = created.id;
    this.awaitingNewIdea = false;

    await this.apiClient.startSession(created.id);
    const latest = await this.apiClient.getSession(created.id);
    await this.sessionManager.saveSession(latest);

    let session = latest;
    const currentMessages = await this.apiClient.getMessages(created.id);
    let responseMessage = this.getLatestResponseForPhase(latest.phase, currentMessages, {});

    if (!responseMessage) {
      const polled = await this.waitForNextResponse(created.id, {});
      session = polled.session;
      responseMessage = polled.responseMessage;
    }

    return { session, responseMessage };
  }

  async submitFollowUp(
    content: string,
    explicitSessionId?: string
  ): Promise<{ session: SessionResponse; responseMessage?: Message }> {
    const sessionId = await this.resolveSessionId(explicitSessionId);
    if (!sessionId) {
      throw new Error('No active session found.');
    }

    const beforeMessages = await this.apiClient.getMessages(sessionId);
    const previousAssistant = getLatestPostDeliveryAssistantMessage(beforeMessages);
    const previousResponseTimestamp = this.getLatestMessageCreatedAt(beforeMessages);

    const updated = await this.apiClient.submitMessage(sessionId, content);
    await this.sessionManager.saveSession(updated);

    this.shellSessionId = sessionId;
    this.awaitingNewIdea = false;

    let latestSession = updated;
    const currentMessages = await this.apiClient.getMessages(sessionId);
    let latest = this.getLatestResponseForPhase(updated.phase, currentMessages, {
      afterCreatedAt: previousResponseTimestamp,
      previousPostDeliveryCreatedAt: previousAssistant?.createdAt
    });

    if (!latest) {
      const polled = await this.waitForNextResponse(sessionId, {
        afterCreatedAt: previousResponseTimestamp,
        previousPostDeliveryCreatedAt: previousAssistant?.createdAt
      });
      latestSession = polled.session;
      latest = polled.responseMessage;
    }

    return {
      session: latestSession,
      responseMessage: latest
    };
  }

  async setParam(key: ShellSettableKey, value: string): Promise<void> {
    const parsed = this.parseSetValue(key, value);
    await this.configManager.set(key, parsed);
  }

  async selectSession(sessionId: string): Promise<SessionResponse> {
    let session = await this.sessionManager.getSession(sessionId);
    if (!session) {
      session = await this.apiClient.getSession(sessionId);
      await this.sessionManager.saveSession(session);
    }

    await this.sessionManager.setCurrentSessionId(sessionId);
    this.shellSessionId = sessionId;
    this.awaitingNewIdea = false;
    return session;
  }

  async clearShellSessionSelection(): Promise<void> {
    this.shellSessionId = null;
    this.awaitingNewIdea = true;
  }

  async getStatus(sessionId?: string): Promise<SessionResponse> {
    const targetSessionId = await this.resolveSessionId(sessionId);
    if (!targetSessionId) {
      throw new Error('No active session found.');
    }

    const session = await this.apiClient.getSession(targetSessionId);
    await this.sessionManager.saveSession(session);
    return session;
  }

  async getArtifact(
    type: ArtifactType,
    options: { refresh: boolean; raw: boolean; sessionId?: string }
  ): Promise<{ sessionId: string; content: string; raw: boolean }> {
    const sessionId = await this.resolveSessionId(options.sessionId);
    if (!sessionId) {
      throw new Error('No active session found.');
    }

    let content: string | null = null;
    if (!options.refresh) {
      content = await this.sessionManager.getArtifact(sessionId, type);
    }

    if (!content) {
      const artifact = await this.apiClient.getArtifact(sessionId, type);
      content = artifact.content;
      await this.sessionManager.saveArtifact(sessionId, type, content);
    }

    return {
      sessionId,
      content,
      raw: options.raw
    };
  }

  async getActiveSession(): Promise<SessionResponse | null> {
    if (this.awaitingNewIdea) {
      return null;
    }

    // For plain-input routing, only use the session explicitly selected/started
    // in this shell run. Do not auto-attach to stale persisted session state.
    const sessionId = this.shellSessionId;
    if (!sessionId) {
      return null;
    }

    const cached = await this.sessionManager.getSession(sessionId);
    if (cached) {
      return cached;
    }

    const live = await this.apiClient.getSession(sessionId);
    await this.sessionManager.saveSession(live);
    return live;
  }

  private parseSetValue(key: ShellSettableKey, value: string): string | number | boolean {
    switch (key) {
      case 'defaultFriction': {
        const parsed = Number(value);
        if (!Number.isFinite(parsed) || parsed < 0 || parsed > 100) {
          throw new Error('defaultFriction must be a number between 0 and 100.');
        }
        return parsed;
      }
      case 'researchEnabled': {
        if (value === 'true' || value === 'on') return true;
        if (value === 'false' || value === 'off') return false;
        throw new Error('researchEnabled must be true/false (or on/off).');
      }
      case 'logLevel': {
        const levels = new Set(['debug', 'info', 'warn', 'error']);
        if (!levels.has(value)) {
          throw new Error('logLevel must be one of: debug, info, warn, error.');
        }
        return value;
      }
      case 'apiUrl':
        try {
          new URL(value);
          return value;
        } catch {
          throw new Error('apiUrl must be a valid URL.');
        }
    }
  }

  private async resolveSessionId(explicitSessionId?: string): Promise<string | null> {
    if (explicitSessionId) {
      return explicitSessionId;
    }

    if (this.shellSessionId) {
      return this.shellSessionId;
    }

    if (this.awaitingNewIdea) {
      return null;
    }

    const current = await this.sessionManager.getCurrentSessionId();
    if (current) {
      return current;
    }

    const sessions = await this.sessionManager.listSessions();
    const latest = [...sessions].sort(
      (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
    )[0];

    return latest?.id ?? null;
  }

  private getLatestMessageCreatedAt(messages: Message[]): string | undefined {
    if (messages.length === 0) {
      return undefined;
    }

    return [...messages].sort(
      (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
    )[0]?.createdAt;
  }

  private getLatestResponseForPhase(
    phase: string,
    messages: Message[],
    options: {
      afterCreatedAt?: string;
      previousPostDeliveryCreatedAt?: string;
    }
  ): Message | undefined {
    if (isPostDeliveryPhase(phase)) {
      return getLatestPostDeliveryAssistantMessage(messages, options.previousPostDeliveryCreatedAt);
    }

    return getLatestResponseMessageAfter(phase, messages, options.afterCreatedAt);
  }

  private async waitForNextResponse(
    sessionId: string,
    options: {
      afterCreatedAt?: string;
      previousPostDeliveryCreatedAt?: string;
    }
  ): Promise<{ session: SessionResponse; responseMessage?: Message }> {
    const deadline = Date.now() + this.followUpTimeoutMs;
    let latestSession = await this.apiClient.getSession(sessionId);
    await this.sessionManager.saveSession(latestSession);

    while (Date.now() < deadline) {
      await new Promise(resolve => setTimeout(resolve, this.followUpPollIntervalMs));
      const [session, messages] = await Promise.all([
        this.apiClient.getSession(sessionId),
        this.apiClient.getMessages(sessionId)
      ]);
      latestSession = session;
      await this.sessionManager.saveSession(session);
      const candidate = this.getLatestResponseForPhase(session.phase, messages, options);
      if (candidate) {
        return { session, responseMessage: candidate };
      }

      const normalizedStatus = normalizeStatus(session.status);
      if (normalizedStatus === 'complete' || normalizedStatus === 'complete_with_gaps') {
        return { session };
      }
    }

    return { session: latestSession };
  }
}
