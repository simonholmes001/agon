/**
 * Agon API Client
 * 
 * HTTP client wrapper for backend API communication.
 * Handles retries, timeouts, error mapping, and logging.
 */

import axios, { type AxiosInstance, type AxiosError } from 'axios';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { Logger } from '../utils/logger.js';
import { AgonError, ErrorCode } from '../utils/error-handler.js';
import {
  encodeAgentModelHeader,
  type RuntimeExecutionProfile,
} from '../runtime/user-runtime-profile.js';
import type { 
  CreateSessionRequest, 
  SessionResponse, 
  Artifact,
  ArtifactType,
  Message,
  SubmitMessageRequest,
  SessionAttachment
} from './types.js';

export interface AuthStatusResponse {
  required: boolean;
  scheme: 'bearer' | 'none';
  authority?: string;
  audience?: string;
  tenantId?: string;
  scope?: string;
  interactiveClientId?: string;
}

export class AgonAPIClient {
  private readonly client: AxiosInstance;
  private readonly maxRetries = 2;
  private readonly logger: Logger;
  private readonly packageName: string;
  private readonly cliVersion: string;
  private runtimeProfile?: RuntimeExecutionProfile;

  constructor(
    baseURL: string = 'http://localhost:5000',
    packageName: string = '@agon_agents/cli',
    cliVersion: string = '0.0.0',
    authToken?: string,
    runtimeProfile?: RuntimeExecutionProfile
  ) {
    this.logger = new Logger('AgonAPIClient');
    this.packageName = packageName;
    this.cliVersion = cliVersion;
    this.runtimeProfile = runtimeProfile;
    // Resolve auth token: explicit parameter > AGON_AUTH_TOKEN env > AGON_BEARER_TOKEN env
    const resolvedAuthToken =
      authToken?.trim() ||
      process.env.AGON_AUTH_TOKEN?.trim() ||
      process.env.AGON_BEARER_TOKEN?.trim();
    const headers: Record<string, string> = {
      'X-Agon-CLI-Version': this.cliVersion,
      'User-Agent': `${this.packageName}/${this.cliVersion}`,
      ...(resolvedAuthToken ? { Authorization: `Bearer ${resolvedAuthToken}` } : {})
    };
    this.applyRuntimeIdentityHeaders(headers, runtimeProfile);

    this.client = axios.create({
      baseURL,
      timeout: 30000, // 30 seconds
      headers
    });

    // Request interceptor for logging
    this.client.interceptors.request.use(
      (config) => {
        this.logger.debug(`${config.method?.toUpperCase()} ${config.url}`, {
          method: config.method,
          url: config.url
        });
        return config;
      },
      (error) => Promise.reject(error)
    );

    // Response interceptor for error handling
    this.client.interceptors.response.use(
      (response) => response,
      async (error: AxiosError) => {
        // Handle retries for network errors
        if (this.shouldRetry(error) && error.config) {
          const retryCount = (error.config as any).__retryCount || 0;
          if (retryCount < this.maxRetries) {
            (error.config as any).__retryCount = retryCount + 1;
            this.logger.warn(`Retrying request (attempt ${retryCount + 1})`, {
              url: error.config.url,
              retryCount: retryCount + 1
            });
            await this.delay(1000 * (retryCount + 1)); // Exponential backoff
            return this.client.request(error.config);
          }
        }
        throw this.mapError(error);
      }
    );
  }

  /**
   * Update runtime profile headers without recreating the client.
   */
  setRuntimeProfile(runtimeProfile: RuntimeExecutionProfile | undefined): void {
    this.runtimeProfile = runtimeProfile;
    const nextHeaders = {
      ...(this.client.defaults.headers.common as Record<string, string>)
    };
    this.clearRuntimeHeaders(nextHeaders);
    this.applyRuntimeIdentityHeaders(nextHeaders, runtimeProfile);
    this.client.defaults.headers.common = nextHeaders;
  }

  /**
   * Create a new debate session
   */
  async createSession(request: CreateSessionRequest): Promise<SessionResponse> {
    // Validation
    if (!request.idea || request.idea.trim().length === 0) {
      throw new AgonError(
        ErrorCode.INVALID_INPUT,
        'Idea cannot be empty',
        ['Provide a description of your idea or decision']
      );
    }
    if (request.idea.trim().length < 10) {
      throw new AgonError(
        ErrorCode.INVALID_INPUT,
        'Idea must be at least 10 characters',
        ['Provide more details about your idea']
      );
    }

    this.logger.debug('Creating new session', { ideaLength: request.idea.length });
    const response = await this.client.post<SessionResponse>(
      '/sessions',
      request,
      { headers: this.getExecutionRequestHeaders() }
    );
    this.logger.debug('Session created successfully', { sessionId: response.data.id });
    return response.data;
  }

  /**
   * Get session by ID
   */
  async getSession(sessionId: string): Promise<SessionResponse> {
    const response = await this.client.get<SessionResponse>(`/sessions/${sessionId}`);
    return response.data;
  }

  /**
   * List sessions for the current user
   */
  async listSessions(): Promise<SessionResponse[]> {
    const response = await this.client.get<SessionResponse[]>('/sessions');
    return response.data;
  }

  /**
   * Start a session (begin debate/clarification)
   */
  async startSession(sessionId: string): Promise<void> {
    this.logger.debug('Starting session', { sessionId });
    await this.client.post(
      `/sessions/${sessionId}/start`,
      undefined,
      { headers: this.getExecutionRequestHeaders() }
    );
    this.logger.debug('Session started successfully', { sessionId });
  }

  /**
   * Get artifact by type
   */
  async getArtifact(sessionId: string, type: ArtifactType): Promise<Artifact> {
    const response = await this.client.get<Artifact>(
      `/sessions/${sessionId}/artifacts/${type}`
    );
    return response.data;
  }

  /**
   * List all artifacts for a session
   */
  async listArtifacts(sessionId: string): Promise<Artifact[]> {
    const response = await this.client.get<Artifact[]>(
      `/sessions/${sessionId}/artifacts`
    );
    return response.data;
  }

  /**
   * Get conversation messages for a session
   */
  async getMessages(sessionId: string): Promise<Message[]> {
    this.logger.debug('Fetching conversation messages', { sessionId });
    const response = await this.client.get<Message[]>(
      `/sessions/${sessionId}/messages`
    );
    this.logger.debug('Messages fetched', { sessionId, count: response.data.length });
    return response.data;
  }

  /**
   * Submit a clarification response or question
   */
  async submitMessage(sessionId: string, content: string): Promise<SessionResponse> {
    // Validation
    if (!content || content.trim().length === 0) {
      throw new AgonError(
        ErrorCode.INVALID_INPUT,
        'Message content cannot be empty',
        ['Provide a response to the clarification question']
      );
    }

    this.logger.debug('Submitting message', { sessionId, contentLength: content.length });
    const request: SubmitMessageRequest = { content };
    const response = await this.client.post<SessionResponse | null>(
      `/sessions/${sessionId}/messages`,
      request,
      {
        timeout: 120000, // Follow-up responses can take longer than default request timeout.
        headers: this.getExecutionRequestHeaders(),
      }
    );
    this.logger.debug('Message submitted successfully', { sessionId });

    // Some backend paths return 202 Accepted with no response body.
    // In that case, fetch the current session state explicitly.
    if (!response.data || typeof response.data !== 'object' || !('phase' in response.data)) {
      return this.getSession(sessionId);
    }

    return response.data;
  }

  /**
   * Upload a file attachment for a session.
   */
  async uploadAttachment(sessionId: string, filePath: string): Promise<SessionAttachment> {
    const resolvedPath = filePath.trim();
    if (!resolvedPath) {
      throw new AgonError(
        ErrorCode.INVALID_INPUT,
        'Attachment path cannot be empty.',
        ['Provide a valid file path']
      );
    }

    let fileStats: Awaited<ReturnType<typeof stat>>;
    try {
      fileStats = await stat(resolvedPath);
    } catch (error) {
      throw this.mapAttachmentFileError(error, resolvedPath);
    }

    if (!fileStats.isFile()) {
      throw new AgonError(
        ErrorCode.INVALID_INPUT,
        'Attachment path must point to a file.',
        ['Provide a file path, not a directory']
      );
    }

    let buffer: Buffer;
    try {
      buffer = await readFile(resolvedPath);
    } catch (error) {
      throw this.mapAttachmentFileError(error, resolvedPath);
    }

    const fileName = path.basename(resolvedPath);
    const contentType = guessContentType(fileName);
    const form = new FormData();
    form.append('file', new Blob([buffer], { type: contentType }), fileName);

    this.logger.debug('Uploading attachment', {
      sessionId,
      fileName,
      sizeBytes: fileStats.size,
      contentType
    });

    const response = await this.client.post<SessionAttachment>(
      `/sessions/${sessionId}/attachments`,
      form,
      { timeout: 120000 }
    );

    return response.data;
  }

  /**
   * List session attachments.
   */
  async listAttachments(sessionId: string): Promise<SessionAttachment[]> {
    const response = await this.client.get<SessionAttachment[]>(
      `/sessions/${sessionId}/attachments`
    );
    return response.data;
  }

  /**
   * Query whether the backend requires authentication.
   * This endpoint is always anonymous — it is safe to call before a token is set up.
   * Returns null when the endpoint is not available (older backend versions).
   */
  async getAuthStatus(): Promise<AuthStatusResponse | null> {
    try {
      const response = await this.client.get<AuthStatusResponse>('/auth/status');
      return response.data;
    } catch {
      return null;
    }
  }

  // Helper methods

  private mapAttachmentFileError(error: unknown, filePath: string): Error {
    const fsError = error as NodeJS.ErrnoException;
    switch (fsError?.code) {
      case 'ENOENT':
        return new AgonError(
          ErrorCode.INVALID_INPUT,
          `Attachment file not found: ${filePath}`,
          [
            'Check that the path exists on disk',
            'Use quotes when the path contains spaces'
          ]
        );
      case 'EACCES':
      case 'EPERM':
        return new AgonError(
          ErrorCode.INVALID_INPUT,
          `Permission denied reading attachment: ${filePath}`,
          [
            'Check file permissions and retry',
            'Choose a file your user account can read'
          ]
        );
      case 'EINVAL':
      case 'ENAMETOOLONG':
        return new AgonError(
          ErrorCode.INVALID_INPUT,
          `Invalid attachment path: ${filePath}`,
          ['Provide a valid local file path']
        );
      default:
        if (error instanceof Error) {
          return error;
        }
        return new AgonError(
          ErrorCode.UNKNOWN,
          'Failed to read attachment file.',
          ['Verify the file path and permissions, then retry']
        );
    }
  }

  private shouldRetry(error: AxiosError): boolean {
    if (error.code === 'ECONNABORTED') return false; // Request timed out.

    const method = error.config?.method?.toLowerCase() ?? '';
    const url = error.config?.url ?? '';
    if (method === 'post' && (url.includes('/messages') || url.includes('/attachments'))) {
      return false; // Avoid duplicate side-effect POST retries.
    }

    // Retry on network errors or 5xx server errors
    if (!error.response) return true; // Network error
    const status = error.response.status;
    return status >= 500 && status < 600;
  }

  private mapError(error: AxiosError): Error {
    if (error.response) {
      const status = error.response.status;
      const data = error.response.data as any;
      const message = data?.detail || data?.message || data?.error || error.message;
      const errorCode = typeof data?.errorCode === 'string' ? data.errorCode : undefined;
      const correlationId = typeof data?.correlationId === 'string' ? data.correlationId : undefined;

      this.logger.error('API error', { status, message, errorCode });

      switch (status) {
        case 401:
        case 403:
          return new AgonError(
            ErrorCode.UNAUTHENTICATED,
            'Authentication required. Your request was rejected by the backend.',
            [
              'Run `agon login` to save your bearer token',
              'Or set the AGON_AUTH_TOKEN environment variable',
              'Contact your Agon administrator if you do not have a token'
            ]
          );
        case 404:
          return new AgonError(
            ErrorCode.SESSION_NOT_FOUND,
            message || 'Session not found',
            ['Check that the session ID is correct', 'Run `agon sessions` to list all sessions']
          );
        case 400:
          return new AgonError(
            ErrorCode.INVALID_INPUT,
            message || 'Invalid request',
            ['Check your input parameters']
          );
        case 429:
          return new AgonError(
            ErrorCode.RATE_LIMIT,
            'Rate limit exceeded. Please try again later.',
            ['Wait a few minutes before retrying']
          );
        case 426:
          return new AgonError(
            ErrorCode.CLI_UPGRADE_REQUIRED,
            'CLI update required by backend policy.',
            [
              'Run `agon --self-update`',
              `Or run \`npm install -g ${this.packageName}@latest\``
            ]
          );
        case 409: {
          const suggestions = [
            'Run /status to check the current session phase',
            'Run /new to start a fresh idea if the session is stuck',
          ];
          if (correlationId) {
            suggestions.push(`Share correlation ID with support: ${correlationId}`);
          }
          return new AgonError(
            ErrorCode.API_ERROR,
            message || 'Operation not allowed in current state.',
            suggestions
          );
        }
        case 500:
        case 502:
        case 503:
        case 504:
          if (status === 503) {
            const attachmentError = this.mapAttachmentServiceUnavailable(errorCode, message);
            if (attachmentError) {
              return attachmentError;
            }
          }

          return new AgonError(
            ErrorCode.BACKEND_UNAVAILABLE,
            message || 'Backend service unavailable. Please try again.',
            [
              'Check if the backend is running',
              'Verify API URL in config',
              'If this happens during long responses, the gateway timeout may be too low'
            ]
          );
        default:
          return new AgonError(ErrorCode.API_ERROR, message);
      }
    }

    // Network error
    this.logger.error('Network error', { message: error.message });

    const timeoutMessage = (error.message || '').toLowerCase().includes('timeout');
    const timeoutSuggestions = [
      'The backend may still be processing your request',
      'Run `/refresh verdict` in shell to check for new assistant output',
      'Retry with the same message if no new response appears'
    ];

    return new AgonError(
      ErrorCode.NETWORK_ERROR,
      error.message || 'Network error',
      timeoutMessage
        ? timeoutSuggestions
        : ['Check your internet connection', 'Verify the API URL is correct']
    );
  }

  private mapAttachmentServiceUnavailable(errorCode: string | undefined, message: string): AgonError | null {
    switch (errorCode) {
      case 'ATTACHMENT_STORAGE_NOT_CONFIGURED':
        return new AgonError(
          ErrorCode.BACKEND_UNAVAILABLE,
          message || 'Attachment storage is not configured.',
          [
            'Configure blob storage for the backend (BLOB_STORAGE_CONNECTION_STRING)',
            'Restart the backend and retry /attach'
          ]
        );
      case 'ATTACHMENT_STORAGE_UNAVAILABLE':
        return new AgonError(
          ErrorCode.BACKEND_UNAVAILABLE,
          message || 'Attachment storage is temporarily unavailable.',
          [
            'Check blob storage connectivity/availability',
            'Retry /attach once storage is healthy'
          ]
        );
      case 'ATTACHMENT_METADATA_NOT_CONFIGURED':
        return new AgonError(
          ErrorCode.BACKEND_UNAVAILABLE,
          message || 'Attachment metadata persistence is not configured.',
          [
            'Enable backend persistence for attachments',
            'Restart the backend and retry /attach'
          ]
        );
      case 'ATTACHMENT_METADATA_UNAVAILABLE':
        return new AgonError(
          ErrorCode.BACKEND_UNAVAILABLE,
          message || 'Attachment metadata persistence is temporarily unavailable.',
          [
            'Check database connectivity',
            'Retry /attach after persistence is healthy'
          ]
        );
      default:
        return null;
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  private applyRuntimeIdentityHeaders(
    headers: Record<string, string>,
    runtimeProfile?: RuntimeExecutionProfile,
  ): void {
    if (!runtimeProfile) {
      return;
    }

    headers['X-Agon-User-Scope'] = runtimeProfile.userScope;
    headers['X-Agon-Agent-Models'] = encodeAgentModelHeader(runtimeProfile.agentModels);
  }

  private clearRuntimeHeaders(headers: Record<string, string>): void {
    delete headers['X-Agon-User-Scope'];
    delete headers['X-Agon-Agent-Models'];

    for (const key of Object.keys(headers)) {
      if (key.toLowerCase().startsWith('x-agon-provider-key-')) {
        delete headers[key];
      }
    }
  }

  private getExecutionRequestHeaders(): Record<string, string> {
    const headers: Record<string, string> = {};
    if (!this.runtimeProfile) {
      return headers;
    }

    for (const [provider, key] of Object.entries(this.runtimeProfile.providerKeys)) {
      if (!key) continue;
      headers[`X-Agon-Provider-Key-${provider}`] = key;
    }

    return headers;
  }
}

function guessContentType(fileName: string): string {
  const extension = path.extname(fileName).toLowerCase();
  const map: Record<string, string> = {
    '.txt': 'text/plain',
    '.md': 'text/markdown',
    '.markdown': 'text/markdown',
    '.json': 'application/json',
    '.csv': 'text/csv',
    '.xml': 'application/xml',
    '.yaml': 'application/x-yaml',
    '.yml': 'application/x-yaml',
    '.html': 'text/html',
    '.htm': 'text/html',
    '.pdf': 'application/pdf',
    '.doc': 'application/msword',
    '.docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    '.xls': 'application/vnd.ms-excel',
    '.xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    '.ppt': 'application/vnd.ms-powerpoint',
    '.pptx': 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.jfif': 'image/jpeg',
    '.gif': 'image/gif',
    '.bmp': 'image/bmp',
    '.tif': 'image/tiff',
    '.tiff': 'image/tiff',
    '.webp': 'image/webp',
    '.heic': 'image/heic',
    '.heif': 'image/heif'
  };

  return map[extension] ?? 'application/octet-stream';
}
