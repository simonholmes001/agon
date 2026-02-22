import { createLogger } from "@/lib/logger";

const logger = createLogger("DebateHubClient");
const SIGNALR_MODULE_NAME = "@microsoft/signalr";
const DEFAULT_BACKEND_BASE_URL = "http://localhost:5000";

export interface RoundProgressEvent {
  sessionId: string;
  phase: string;
}

export interface TruthMapPatchEvent {
  sessionId: string;
  version: number;
  patch: unknown;
}

export interface TranscriptMessageEvent {
  id: string;
  type: string;
  agentId?: string;
  content: string;
  round: number;
  isStreaming: boolean;
  createdAtUtc: string;
}

export interface DebateHubConnection {
  start: () => Promise<void>;
  stop: () => Promise<void>;
  onRoundProgress: (handler: (event: RoundProgressEvent) => void) => void;
  onTruthMapPatch: (handler: (event: TruthMapPatchEvent) => void) => void;
  onTranscriptMessage: (handler: (event: TranscriptMessageEvent) => void) => void;
  onReconnected: (handler: () => void) => void;
}

export function createDebateHubConnection(sessionId: string): DebateHubConnection {
  return new LazyDebateHubConnection(sessionId);
}

export function resolveDebateHubUrl(): string {
  const configuredHubUrl = process.env.NEXT_PUBLIC_DEBATE_HUB_URL?.trim();
  if (configuredHubUrl) {
    return configuredHubUrl;
  }

  const backendBaseUrl = (
    process.env.NEXT_PUBLIC_BACKEND_BASE_URL ??
    process.env.BACKEND_API_BASE_URL ??
    DEFAULT_BACKEND_BASE_URL
  ).replace(/\/+$/, "");

  return `${backendBaseUrl}/hubs/debate`;
}

class LazyDebateHubConnection implements DebateHubConnection {
  private readonly hubUrl: string;
  private connection: HubConnectionLike | null = null;
  private readonly roundProgressHandlers: Array<(event: RoundProgressEvent) => void> = [];
  private readonly truthMapPatchHandlers: Array<(event: TruthMapPatchEvent) => void> = [];
  private readonly transcriptMessageHandlers: Array<(event: TranscriptMessageEvent) => void> = [];
  private readonly reconnectedHandlers: Array<() => void> = [];

  constructor(private readonly sessionId: string) {
    this.hubUrl = resolveDebateHubUrl();
  }

  async start() {
    const connection = await this.ensureConnection();
    if (!connection) return;

    try {
      await connection.start();
      logger.info("signalr connected", { sessionId: this.sessionId, hubUrl: this.hubUrl });
      await connection.invoke("JoinSession", this.sessionId);
      logger.info("signalr session subscription registered", { sessionId: this.sessionId });
    } catch (error) {
      logger.warn(
        "failed to start signalr connection",
        { sessionId: this.sessionId, hubUrl: this.hubUrl },
        error,
      );
      throw error;
    }
  }

  async stop() {
    if (!this.connection) return;
    try {
      await leaveSession(this.connection, this.sessionId);
      await this.connection.stop();
      logger.info("signalr disconnected", { sessionId: this.sessionId, hubUrl: this.hubUrl });
    } catch (error) {
      logger.warn(
        "failed to stop signalr connection",
        { sessionId: this.sessionId, hubUrl: this.hubUrl },
        error,
      );
      throw error;
    }
  }

  onRoundProgress(handler: (event: RoundProgressEvent) => void) {
    this.roundProgressHandlers.push(handler);
    this.connection?.on("RoundProgress", handler);
  }

  onTruthMapPatch(handler: (event: TruthMapPatchEvent) => void) {
    this.truthMapPatchHandlers.push(handler);
    this.connection?.on("TruthMapPatch", handler);
  }

  onTranscriptMessage(handler: (event: TranscriptMessageEvent) => void) {
    this.transcriptMessageHandlers.push(handler);
    this.connection?.on("TranscriptMessage", handler);
  }

  onReconnected(handler: () => void) {
    this.reconnectedHandlers.push(handler);
  }

  private async ensureConnection(): Promise<HubConnectionLike | null> {
    if (this.connection) return this.connection;

    const signalR = await importSignalRModule();
    if (!signalR) return null;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(this.hubUrl, {
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build() as HubConnectionLike;

    for (const handler of this.roundProgressHandlers) {
      connection.on("RoundProgress", handler);
    }
    for (const handler of this.truthMapPatchHandlers) {
      connection.on("TruthMapPatch", handler);
    }
    for (const handler of this.transcriptMessageHandlers) {
      connection.on("TranscriptMessage", handler);
    }

    connection.onreconnected(async () => {
      logger.warn("signalr reconnected", { sessionId: this.sessionId });
      await joinSession(connection, this.sessionId);
      for (const handler of this.reconnectedHandlers) {
        handler();
      }
    });

    this.connection = connection;
    return connection;
  }
}

async function importSignalRModule(): Promise<SignalRModule | null> {
  try {
    return await import(SIGNALR_MODULE_NAME) as SignalRModule;
  } catch (error) {
    logger.warn(
      "signalr client module is unavailable",
      { moduleName: SIGNALR_MODULE_NAME },
      error,
    );
    return null;
  }
}

async function joinSession(connection: HubConnectionLike, sessionId: string) {
  try {
    await connection.invoke("JoinSession", sessionId);
  } catch (error) {
    logger.warn("failed to join signalr session", { sessionId }, error);
  }
}

async function leaveSession(connection: HubConnectionLike, sessionId: string) {
  try {
    await connection.invoke("LeaveSession", sessionId);
  } catch (error) {
    logger.warn("failed to leave signalr session", { sessionId }, error);
  }
}

interface HubConnectionLike {
  start: () => Promise<void>;
  stop: () => Promise<void>;
  invoke: (methodName: string, ...args: unknown[]) => Promise<unknown>;
  on: (methodName: string, newMethod: (...args: unknown[]) => void) => void;
  onreconnected: (callback: (() => void | Promise<void>) | null) => void;
}

interface SignalRModule {
  HubConnectionBuilder: new () => SignalRHubBuilder;
  HttpTransportType: {
    WebSockets: number;
    LongPolling: number;
  };
  LogLevel: {
    Warning: number;
  };
}

interface SignalRHubBuilder {
  withUrl: (url: string, options: { transport: number }) => SignalRHubBuilder;
  withAutomaticReconnect: (retryDelays: number[]) => SignalRHubBuilder;
  configureLogging: (level: number) => SignalRHubBuilder;
  build: () => HubConnectionLike;
}
