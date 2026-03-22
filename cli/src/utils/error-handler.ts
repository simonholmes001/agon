/**
 * Error Handler
 * 
 * Error formatting and classification for CLI.
 * Provides friendly error messages with recovery suggestions.
 */

import chalk from 'chalk';

export enum ErrorCode {
  NETWORK_ERROR = 'NETWORK_ERROR',
  API_ERROR = 'API_ERROR',
  SESSION_NOT_FOUND = 'SESSION_NOT_FOUND',
  INVALID_INPUT = 'INVALID_INPUT',
  RATE_LIMIT = 'RATE_LIMIT',
  BACKEND_UNAVAILABLE = 'BACKEND_UNAVAILABLE',
  CLI_UPGRADE_REQUIRED = 'CLI_UPGRADE_REQUIRED',
  UNAUTHENTICATED = 'UNAUTHENTICATED',
  UNKNOWN = 'UNKNOWN',
}

export class AgonError extends Error {
  constructor(
    public readonly code: ErrorCode,
    message: string,
    public readonly suggestions?: string[]
  ) {
    super(message);
    this.name = 'AgonError';
    Object.setPrototypeOf(this, AgonError.prototype);
  }
}

/**
 * Format an error for display in the CLI
 */
export function formatError(error: unknown): string {
  if (error instanceof AgonError) {
    let message = chalk.red(`✗ ${error.message}`);
    
    if (error.suggestions && error.suggestions.length > 0) {
      message += '\n\n' + chalk.yellow('💡 Suggestions:');
      for (const suggestion of error.suggestions) {
        message += '\n  • ' + suggestion;
      }
    }
    
    return message;
  }
  
  if (error instanceof Error) {
    return chalk.red(`✗ ${error.message}`);
  }
  
  if (typeof error === 'string') {
    return chalk.red(`✗ ${error}`);
  }
  
  return chalk.red('✗ An unknown error occurred');
}

/**
 * Check if error is a network error
 */
export function isNetworkError(error: unknown): boolean {
  if (error instanceof AgonError) {
    return error.code === ErrorCode.NETWORK_ERROR;
  }
  
  if (error instanceof Error) {
    const message = error.message.toLowerCase();
    return (
      message.includes('enotfound') ||
      message.includes('econnrefused') ||
      message.includes('network')
    );
  }
  
  return false;
}

/**
 * Check if error is an API error
 */
export function isApiError(error: unknown): boolean {
  if (error instanceof AgonError) {
    return (
      error.code === ErrorCode.API_ERROR ||
      error.code === ErrorCode.BACKEND_UNAVAILABLE ||
      error.code === ErrorCode.RATE_LIMIT ||
      error.code === ErrorCode.CLI_UPGRADE_REQUIRED
    );
  }
  
  return false;
}
