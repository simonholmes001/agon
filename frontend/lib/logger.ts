// ─── Structured Logger ───────────────────────────────────────────────────────
// Environment-aware, component-scoped logger.
// Use createLogger("ComponentName") — never bare console.* calls.
//
// Silent in test environment by default. Override with explicit LogLevel.

export enum LogLevel {
  DEBUG = 0,
  INFO = 1,
  WARN = 2,
  ERROR = 3,
  SILENT = 4,
}

export interface Logger {
  debug(message: string, context?: Record<string, unknown>): void;
  info(message: string, context?: Record<string, unknown>): void;
  warn(message: string, context?: Record<string, unknown>): void;
  error(
    message: string,
    context?: Record<string, unknown>,
    error?: unknown,
  ): void;
}

function resolveDefaultLevel(): LogLevel {
  if (typeof process !== "undefined" && process.env.NODE_ENV === "test") {
    return LogLevel.SILENT;
  }
  if (
    typeof process !== "undefined" &&
    process.env.NODE_ENV === "production"
  ) {
    return LogLevel.WARN;
  }
  return LogLevel.DEBUG;
}

function formatPrefix(component: string): string {
  return `[${component}]`;
}

export function createLogger(
  component: string,
  level?: LogLevel,
): Logger {
  const activeLevel = level ?? resolveDefaultLevel();
  const prefix = formatPrefix(component);

  return {
    debug(message: string, context?: Record<string, unknown>) {
      if (activeLevel > LogLevel.DEBUG) return;
      if (context) {
        console.debug(prefix, message, context);
      } else {
        console.debug(prefix, message);
      }
    },

    info(message: string, context?: Record<string, unknown>) {
      if (activeLevel > LogLevel.INFO) return;
      if (context) {
        console.info(prefix, message, context);
      } else {
        console.info(prefix, message);
      }
    },

    warn(message: string, context?: Record<string, unknown>) {
      if (activeLevel > LogLevel.WARN) return;
      if (context) {
        console.warn(prefix, message, context);
      } else {
        console.warn(prefix, message);
      }
    },

    error(
      message: string,
      context?: Record<string, unknown>,
      err?: unknown,
    ) {
      if (activeLevel > LogLevel.ERROR) return;
      if (context && err) {
        console.error(prefix, message, context, err);
      } else if (context) {
        console.error(prefix, message, context);
      } else {
        console.error(prefix, message);
      }
    },
  };
}
