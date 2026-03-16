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

export function parseShellInput(input: string): ParsedShellInput {
  const trimmed = input.trim();

  if (!trimmed.startsWith('/')) {
    return {
      type: 'plain',
      text: trimmed
    };
  }

  const [commandToken, ...rawArgs] = trimmed.split(/\s+/);
  const command = commandToken.slice(1).toLowerCase();

  switch (command) {
    case 'help':
      return { type: 'slash', command: 'help' };
    case 'params':
      return { type: 'slash', command: 'params' };
    case 'new':
      return { type: 'slash', command: 'new' };
    case 'show-sessions':
    case 'showsessions':
    case 'sessions':
      return { type: 'slash', command: 'show-sessions' };
    case 'attach':
    case 'image':
      return parseAttach(rawArgs);
    case 'set':
      return parseSet(rawArgs);
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

function parseAttach(args: string[]): ParsedShellInput {
  const rawPath = args.join(' ').trim();
  if (!rawPath) {
    return {
      type: 'error',
      message: 'Usage: /attach <file-path>'
    };
  }

  return {
    type: 'slash',
    command: 'attach',
    path: stripMatchingQuotes(rawPath)
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
