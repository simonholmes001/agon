import type { ArtifactType } from '../api/types.js';
import type { ParsedShellInput, ShellSettableKey } from './types.js';

const artifactTypes = new Set<ArtifactType>([
  'verdict',
  'plan',
  'prd',
  'risks',
  'assumptions',
  'architecture',
  'copilot'
]);

const settableKeys = new Set<ShellSettableKey>([
  'apiUrl',
  'defaultFriction',
  'researchEnabled',
  'logLevel'
]);
const attachUsageMessage = 'Usage: /attach <file-path>';

export function parseShellInput(input: string): ParsedShellInput {
  const trimmed = input.trim();

  if (!trimmed.startsWith('/')) {
    return {
      type: 'plain',
      text: trimmed
    };
  }

  const [commandToken, ...rawArgs] = trimmed.split(/\s+/);
  const rawCommandArgs = trimmed.slice(commandToken.length).trimStart();
  const command = commandToken.slice(1).toLowerCase();

  switch (command) {
    case 'help':
      return { type: 'slash', command: 'help' };
    case 'params':
      return { type: 'slash', command: 'params' };
    case 'new':
      return { type: 'slash', command: 'new' };
    case 'update':
      return parseUpdate(rawArgs);
    case 'show-sessions':
    case 'showsessions':
    case 'sessions':
      return { type: 'slash', command: 'show-sessions' };
    case 'attach':
    case 'image':
      return parseAttach(rawCommandArgs);
    case 'set':
      return parseSet(rawArgs);
    case 'unset':
      return parseUnset(rawArgs);
    case 'session':
      return parseSession(rawArgs);
    case 'resume':
      return parseResume(rawArgs);
    case 'status':
      return parseStatus(rawArgs);
    case 'show':
      return parseShow(rawArgs);
    case 'refresh':
      return parseRefresh(rawArgs);
    case 'follow-up':
    case 'followup':
      return parseFollowUp(rawArgs);
    default:
      return {
        type: 'error',
        message: 'Unknown command. Use /help for available commands.'
      };
  }
}

export type InlineAttachExtraction =
  | {
      type: 'attach';
      path: string;
      remainingText: string;
    }
  | {
      type: 'error';
      message: string;
    };

export function extractInlineAttach(input: string): InlineAttachExtraction | null {
  const match = /(?:^|\s)\/(?:attach|image)(?=\s|$)/i.exec(input);
  if (!match || typeof match.index !== 'number') {
    return null;
  }

  const commandMatch = match[0];
  const leadingWhitespaceLength = commandMatch.length - commandMatch.trimStart().length;
  const commandStart = match.index + leadingWhitespaceLength;
  let pathStart = match.index + commandMatch.length;

  while (pathStart < input.length && /\s/.test(input[pathStart] ?? '')) {
    pathStart += 1;
  }

  if (pathStart >= input.length) {
    return {
      type: 'error',
      message: attachUsageMessage
    };
  }

  const parsedPath = parseAttachPathToken(input, pathStart);
  if (!parsedPath) {
    return {
      type: 'error',
      message: attachUsageMessage
    };
  }

  const remainingText = normalizeInlineMessageText(`${input.slice(0, commandStart)} ${input.slice(parsedPath.nextIndex)}`);

  return {
    type: 'attach',
    path: parsedPath.path,
    remainingText
  };
}

function parseSet(args: string[]): ParsedShellInput {
  if (args.length < 2) {
    return {
      type: 'error',
      message: 'Usage: /set <apiUrl|defaultFriction|researchEnabled|logLevel> <value>'
    };
  }

  const key = args[0] as ShellSettableKey;
  if (!settableKeys.has(key)) {
    return {
      type: 'error',
      message: 'Usage: /set <apiUrl|defaultFriction|researchEnabled|logLevel> <value>'
    };
  }

  return {
    type: 'slash',
    command: 'set',
    key,
    value: args.slice(1).join(' ')
  };
}

function parseUnset(args: string[]): ParsedShellInput {
  if (args.length !== 1) {
    return {
      type: 'error',
      message: 'Usage: /unset <apiUrl|defaultFriction|researchEnabled|logLevel>'
    };
  }

  const key = args[0] as ShellSettableKey;
  if (!settableKeys.has(key)) {
    return {
      type: 'error',
      message: 'Usage: /unset <apiUrl|defaultFriction|researchEnabled|logLevel>'
    };
  }

  return {
    type: 'slash',
    command: 'unset',
    key
  };
}

function parseUpdate(args: string[]): ParsedShellInput {
  if (args.length === 0) {
    return {
      type: 'slash',
      command: 'update',
      check: false
    };
  }

  if (args.length === 1 && args[0] === '--check') {
    return {
      type: 'slash',
      command: 'update',
      check: true
    };
  }

  return {
    type: 'error',
    message: 'Usage: /update [--check]'
  };
}

function parseSession(args: string[]): ParsedShellInput {
  if (args.length !== 1) {
    return {
      type: 'error',
      message: 'Usage: /session <session-id>'
    };
  }

  return {
    type: 'slash',
    command: 'session',
    sessionId: args[0]
  };
}

function parseResume(args: string[]): ParsedShellInput {
  if (args.length > 1) {
    return {
      type: 'error',
      message: 'Usage: /resume [session-id]'
    };
  }

  return {
    type: 'slash',
    command: 'resume',
    sessionId: args[0]
  };
}

function parseStatus(args: string[]): ParsedShellInput {
  if (args.length > 1) {
    return {
      type: 'error',
      message: 'Usage: /status [session-id]'
    };
  }

  return {
    type: 'slash',
    command: 'status',
    sessionId: args[0]
  };
}

function parseShow(args: string[]): ParsedShellInput {
  if (args.length === 0) {
    return {
      type: 'error',
      message: 'Usage: /show <verdict|plan|prd|risks|assumptions|architecture|copilot> [--refresh] [--raw]'
    };
  }

  const artifactType = args[0] as ArtifactType;
  if (!artifactTypes.has(artifactType)) {
    return {
      type: 'error',
      message: 'Usage: /show <verdict|plan|prd|risks|assumptions|architecture|copilot> [--refresh] [--raw]'
    };
  }

  const flagSet = new Set(args.slice(1));
  const unknownFlags = [...flagSet].filter(flag => flag !== '--refresh' && flag !== '--raw');
  if (unknownFlags.length > 0) {
    return {
      type: 'error',
      message: 'Usage: /show <verdict|plan|prd|risks|assumptions|architecture|copilot> [--refresh] [--raw]'
    };
  }

  return {
    type: 'slash',
    command: 'show',
    artifactType,
    refresh: flagSet.has('--refresh'),
    raw: flagSet.has('--raw')
  };
}

function parseFollowUp(args: string[]): ParsedShellInput {
  if (args.length === 0) {
    return {
      type: 'error',
      message: 'Usage: /follow-up <message>'
    };
  }

  return {
    type: 'slash',
    command: 'follow-up',
    message: args.join(' ')
  };
}

function parseAttach(rawArgs: string): ParsedShellInput {
  const rawPath = rawArgs.trim();
  if (!rawPath) {
    return {
      type: 'error',
      message: attachUsageMessage
    };
  }

  const parsedPath = parseAttachPathToken(rawPath, 0);
  if (!parsedPath) {
    return {
      type: 'error',
      message: attachUsageMessage
    };
  }

  if (rawPath.slice(parsedPath.nextIndex).trim().length > 0) {
    return {
      type: 'error',
      message: attachUsageMessage
    };
  }

  return {
    type: 'slash',
    command: 'attach',
    path: parsedPath.path
  };
}

function parseRefresh(args: string[]): ParsedShellInput {
  if (args.length > 1) {
    return {
      type: 'error',
      message: 'Usage: /refresh [verdict|plan|prd|risks|assumptions|architecture|copilot]'
    };
  }

  if (args.length === 1) {
    const artifactType = args[0] as ArtifactType;
    if (!artifactTypes.has(artifactType)) {
      return {
        type: 'error',
        message: 'Usage: /refresh [verdict|plan|prd|risks|assumptions|architecture|copilot]'
      };
    }

    return {
      type: 'slash',
      command: 'refresh',
      artifactType
    };
  }

  return {
    type: 'slash',
    command: 'refresh',
    artifactType: undefined
  };
}

function stripMatchingQuotes(value: string): string {
  if (value.length >= 2) {
    const first = value[0];
    const last = value[value.length - 1];
    if ((first === '"' && last === '"') || (first === '\'' && last === '\'')) {
      return value.slice(1, -1);
    }
  }
  return value;
}

function parseAttachPathToken(
  input: string,
  startIndex: number
): { path: string; nextIndex: number } | null {
  let cursor = startIndex;
  while (cursor < input.length && /\s/.test(input[cursor] ?? '')) {
    cursor += 1;
  }

  if (cursor >= input.length) {
    return null;
  }

  const firstPathChar = input[cursor];
  let rawPath = '';
  let nextIndex = cursor;

  if (firstPathChar === '"' || firstPathChar === '\'') {
    const quote = firstPathChar;
    let pathEnd = cursor + 1;
    while (pathEnd < input.length) {
      const char = input[pathEnd];
      if (char === quote && input[pathEnd - 1] !== '\\') {
        break;
      }
      pathEnd += 1;
    }

    if (pathEnd >= input.length) {
      return null;
    }

    rawPath = input.slice(cursor + 1, pathEnd);
    nextIndex = pathEnd + 1;
  } else {
    while (nextIndex < input.length && !/\s/.test(input[nextIndex] ?? '')) {
      nextIndex += 1;
    }
    rawPath = input.slice(cursor, nextIndex);
  }

  const normalizedPath = rawPath.trim();
  if (!normalizedPath) {
    return null;
  }

  return {
    path: stripMatchingQuotes(normalizedPath),
    nextIndex
  };
}

function normalizeInlineMessageText(value: string): string {
  return value
    .replace(/\s+/g, ' ')
    .replace(/\s+([.,!?;:])/g, '$1')
    .trim();
}
