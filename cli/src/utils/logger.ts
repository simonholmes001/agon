/**
 * Structured Logger
 * 
 * Environment-aware logging utility for CLI.
 * - Silent in test environment by default
 * - Structured logging with context
 * - Configurable log levels
 */

export enum LogLevel {
  DEBUG = 0,
  INFO = 1,
  WARN = 2,
  ERROR = 3,
  SILENT = 4,
}

export class Logger {
  private readonly component: string;
  private readonly level: LogLevel;

  constructor(component: string, level?: LogLevel) {
    this.component = component;
    
    // Default to SILENT in test environment, INFO otherwise
    if (level === undefined) {
      this.level = process.env.NODE_ENV === 'test' ? LogLevel.SILENT : LogLevel.INFO;
    } else {
      this.level = level;
    }
  }

  debug(message: string, context?: Record<string, any>): void {
    if (this.level <= LogLevel.DEBUG) {
      this.log(message, context);
    }
  }

  info(message: string, context?: Record<string, any>): void {
    if (this.level <= LogLevel.INFO) {
      this.log(message, context);
    }
  }

  warn(message: string, context?: Record<string, any>): void {
    if (this.level <= LogLevel.WARN) {
      this.logWarn(message, context);
    }
  }

  error(message: string, context?: Record<string, any>): void {
    if (this.level <= LogLevel.ERROR) {
      this.logError(message, context);
    }
  }

  private log(message: string, context?: Record<string, any>): void {
    const prefix = `[${this.component}]`;
    if (context) {
      console.log(prefix, message, context);
    } else {
      console.log(prefix, message);
    }
  }

  private logWarn(message: string, context?: Record<string, any>): void {
    const prefix = `[${this.component}]`;
    if (context) {
      console.warn(prefix, message, context);
    } else {
      console.warn(prefix, message);
    }
  }

  private logError(message: string, context?: Record<string, any>): void {
    const prefix = `[${this.component}]`;
    if (context) {
      console.error(prefix, message, context);
    } else {
      console.error(prefix, message);
    }
  }
}

// Create default logger instance
export const logger = new Logger('agon');
