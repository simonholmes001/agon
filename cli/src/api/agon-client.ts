/**
 * Agon API Client
 * 
 * HTTP client wrapper for backend API communication.
 * Handles retries, timeouts, error mapping, and logging.
 */

import axios, { type AxiosInstance, type AxiosError } from 'axios';
import { Logger } from '../utils/logger.js';
import { AgonError, ErrorCode } from '../utils/error-handler.js';
import type { 
  CreateSessionRequest, 
  SessionResponse, 
  ClarificationResponse,
  SubmitAnswersRequest,
  Artifact,
  ArtifactType,
  Message,
  SubmitMessageRequest
} from './types.js';

export class AgonAPIClient {
  private readonly client: AxiosInstance;
  private readonly maxRetries = 2;
  private readonly logger: Logger;
  private readonly packageName: string;
  private readonly cliVersion: string;

  constructor(
    baseURL: string = 'http://localhost:5000',
    packageName: string = '@agon_agents/cli',
    cliVersion: string = '0.0.0'
  ) {
    this.logger = new Logger('AgonAPIClient');
    this.packageName = packageName;
    this.cliVersion = cliVersion;
    this.client = axios.create({
      baseURL,
      timeout: 30000, // 30 seconds
      headers: {
        'Content-Type': 'application/json',
        'X-Agon-CLI-Version': this.cliVersion,
        'User-Agent': `${this.packageName}/${this.cliVersion}`
      }
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
    const response = await this.client.post<SessionResponse>('/sessions', request);
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
   * Start a session (begin debate/clarification)
   */
  async startSession(sessionId: string): Promise<void> {
    this.logger.debug('Starting session', { sessionId });
    await this.client.post(`/sessions/${sessionId}/start`);
    this.logger.debug('Session started successfully', { sessionId });
  }

  /**
   * Get clarification questions for a session
   */
  async getClarification(sessionId: string): Promise<ClarificationResponse> {
    const response = await this.client.get<ClarificationResponse>(
      `/sessions/${sessionId}/clarification`
    );
    return response.data;
  }

  /**
   * Submit clarification answers
   */
  async submitAnswers(
    sessionId: string, 
    request: SubmitAnswersRequest
  ): Promise<SessionResponse> {
    const response = await this.client.post<SessionResponse>(
      `/sessions/${sessionId}/messages`,
      request
    );
    return response.data;
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
      { timeout: 120000 } // Follow-up responses can take longer than default request timeout.
    );
    this.logger.debug('Message submitted successfully', { sessionId });

    // Some backend paths return 202 Accepted with no response body.
    // In that case, fetch the current session state explicitly.
    if (!response.data || typeof response.data !== 'object' || !('phase' in response.data)) {
      return this.getSession(sessionId);
    }

    return response.data;
  }

  // Helper methods

  private shouldRetry(error: AxiosError): boolean {
    if (error.code === 'ECONNABORTED') return false; // Request timed out.

    const method = error.config?.method?.toLowerCase() ?? '';
    const url = error.config?.url ?? '';
    if (method === 'post' && url.includes('/messages')) {
      return false; // Avoid duplicate message submission retries.
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
      const message = data?.detail || data?.message || error.message;

      this.logger.error('API error', { status, message });

      switch (status) {
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
        case 500:
        case 502:
        case 503:
        case 504:
          return new AgonError(
            ErrorCode.BACKEND_UNAVAILABLE,
            'Backend service unavailable. Please try again.',
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
      'Run `agon show verdict --refresh` to check for new assistant output',
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

  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}
