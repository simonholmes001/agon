import type { ArtifactType } from '../api/types.js';

export type ShellSettableKey =
  | 'apiUrl'
  | 'defaultFriction'
  | 'researchEnabled'
  | 'logLevel';

export type ParsedShellInput =
  | {
      type: 'plain';
      text: string;
    }
  | {
      type: 'error';
      message: string;
    }
  | {
      type: 'slash';
      command: 'help' | 'params' | 'new' | 'show-sessions';
    }
  | {
      type: 'slash';
      command: 'update';
      check: boolean;
    }
  | {
      type: 'slash';
      command: 'attach';
      path: string;
    }
  | {
      type: 'slash';
      command: 'session' | 'status' | 'resume';
      sessionId?: string;
    }
  | {
      type: 'slash';
      command: 'set';
      key: ShellSettableKey;
      value: string;
    }
  | {
      type: 'slash';
      command: 'show';
      artifactType: ArtifactType;
      refresh: boolean;
      raw: boolean;
    }
  | {
      type: 'slash';
      command: 'refresh';
      artifactType?: ArtifactType;
    }
  | {
      type: 'slash';
      command: 'follow-up';
      message: string;
    };
