/**
 * Agon API Client
 * 
 * HTTP client wrapper for backend API communication.
 * Handles retries, timeouts, error mapping, and logging.
 */

import axios, { type AxiosInstance, type AxiosError } from 'axios';
import type { 
  CreateSessionRequest, 
  SessionResponse, 
  ClarificationResponse,
  SubmitAnswersRequest,
  Artifact,
  ArtifactType
} from './types.js';

export class AgonAPIClient {
  private client: AxiosInstance;
  private maxRetries = 2;

  constructor(baseURL: string = 'http://localhost:5000') {
    this.client = axios.create({
      baseURL,
      timeout: 30000, // 30 seconds
      headers: {
        'Content-Type': 'application/json'
      }
    });

    // Request interceptor for logging
    this.client.interceptors.request.use(
      (config) => {
        // TODO: Add structured logging
        console.log(`[API] ${config.method?.toUpperCase()} ${config.url}`);
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
            await this.delay(1000 * (retryCount + 1)); // Exponential backoff
            return this.client.request(error.config);
          }
        }
        return Promise.reject(this.mapError(error));
      }
    );
  }

  /**
   * Create a new debate session
   */
  async createSession(request: CreateSessionRequest): Promise<SessionResponse> {
    // Validation
    if (!request.idea || request.idea.trim().length === 0) {
      throw new Error('Idea cannot be empty');
    }
    if (request.idea.trim().length < 10) {
      throw new Error('Idea must be at least 10 characters');
    }

    const response = await this.client.post<SessionResponse>('/sessions', request);
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

  // Helper methods

  private shouldRetry(error: AxiosError): boolean {
    // Retry on network errors or 5xx server errors
    if (!error.response) return true; // Network error
    const status = error.response.status;
    return status >= 500 && status < 600;
  }

  private mapError(error: AxiosError): Error {
    if (error.response) {
      const status = error.response.status;
      const data = error.response.data as any;
      const message = data?.message || error.message;

      switch (status) {
        case 404:
          return new Error(message || 'Session not found');
        case 400:
          return new Error(message || 'Invalid request');
        case 429:
          return new Error('Rate limit exceeded. Please try again later.');
        case 500:
        case 502:
        case 503:
          return new Error('Backend service unavailable. Please try again.');
        default:
          return new Error(message);
      }
    }

    // Network error
    return new Error(error.message || 'Network error');
  }

  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}
