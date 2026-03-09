import type { SessionResponse } from '../api/types.js';
import { parseShellInput } from './parser.js';
import type { PlainInputRoute } from './router.js';

interface ShellControllerLike {
  getParamsSnapshot(): Promise<{
    config: {
      apiUrl: string;
      defaultFriction: number;
      researchEnabled: boolean;
      logLevel: 'debug' | 'info' | 'warn' | 'error';
    };
    session: SessionResponse | null;
  }>;
  setParam(key: 'apiUrl' | 'defaultFriction' | 'researchEnabled' | 'logLevel', value: string): Promise<void>;
  clearShellSessionSelection(): Promise<void>;
  selectSession(sessionId: string): Promise<SessionResponse>;
  getStatus(sessionId?: string): Promise<SessionResponse>;
  getArtifact(
    type: 'verdict' | 'plan' | 'prd' | 'risks' | 'assumptions' | 'architecture' | 'copilot',
    options: { refresh: boolean; raw: boolean; sessionId?: string }
  ): Promise<{ sessionId: string; content: string; raw: boolean }>;
  submitFollowUp(content: string): Promise<{
    session: SessionResponse;
    responseMessage?: { agentId?: string; message: string };
  }>;
  startIdea(idea: string): Promise<{
    session: SessionResponse;
    responseMessage?: { agentId?: string; message: string };
  }>;
  getActiveSession(): Promise<SessionResponse | null>;
}

export interface ShellEngineDeps {
  controller: ShellControllerLike;
  routePlainInput: (session: SessionResponse | null) => PlainInputRoute;
  print: (line: string) => void;
}

export type ShellEngineOutcome =
  | { kind: 'noop' }
  | { kind: 'started'; sessionId: string; response?: { agentId?: string; message: string } }
  | { kind: 'follow-up'; response?: { agentId?: string; message: string } }
  | { kind: 'status'; status: string; phase: string; sessionId: string }
  | { kind: 'artifact'; content: string; raw: boolean; sessionId: string };

export class ShellEngine {
  private readonly controller: ShellControllerLike;
  private readonly routePlainInputFn: (session: SessionResponse | null) => PlainInputRoute;
  private readonly print: (line: string) => void;

  constructor(deps: ShellEngineDeps) {
    this.controller = deps.controller;
    this.routePlainInputFn = deps.routePlainInput;
    this.print = deps.print;
  }

  async handleInput(input: string): Promise<ShellEngineOutcome> {
    const parsed = parseShellInput(input);

    if (parsed.type === 'error') {
      this.print(parsed.message);
      return { kind: 'noop' };
    }

    if (parsed.type === 'slash') {
      return this.handleSlash(parsed);
    }

    const activeSession = await this.controller.getActiveSession();
    const route = this.routePlainInputFn(activeSession);

    if (route.action === 'start') {
      const started = await this.controller.startIdea(parsed.text);
      this.print(`Session started: ${started.session.id}`);
      return {
        kind: 'started',
        sessionId: started.session.id,
        response: started.responseMessage
          ? {
              agentId: started.responseMessage.agentId,
              message: started.responseMessage.message
            }
          : undefined
      };
    }

    if (route.action === 'follow-up') {
      const followUp = await this.controller.submitFollowUp(parsed.text);
      return {
        kind: 'follow-up',
        response: followUp.responseMessage
          ? {
              agentId: followUp.responseMessage.agentId,
              message: followUp.responseMessage.message
            }
          : undefined
      };
    }

    this.print(route.reason);
    return { kind: 'noop' };
  }

  private async handleSlash(
    parsed: Extract<ReturnType<typeof parseShellInput>, { type: 'slash' }>
  ): Promise<ShellEngineOutcome> {
    switch (parsed.command) {
      case 'help':
        this.print('Commands:');
        this.print('  /help                         Show this command reference');
        this.print('  /params                       Show current shell parameters and active session');
        this.print('  /set <key> <value>            Persist config key (apiUrl|defaultFriction|researchEnabled|logLevel)');
        this.print('  /new                          Reset shell to awaiting-idea mode');
        this.print('  /session <session-id>         Switch active session');
        this.print('  /status [session-id]          Fetch session status/phase');
        this.print('  /show <artifact> [flags]      Show artifact (e.g. verdict, prd)');
        this.print('  /follow-up <message>          Send explicit follow-up message');
        this.print('Examples:');
        this.print('  /set defaultFriction 75');
        this.print('  /set researchEnabled false');
        this.print('  /set logLevel warn');
        this.print('  /set apiUrl http://localhost:5000');
        return { kind: 'noop' };
      case 'params': {
        const snapshot = await this.controller.getParamsSnapshot();
        this.print('Current parameters:');
        this.print(`  apiUrl: ${snapshot.config.apiUrl}`);
        this.print(`  defaultFriction: ${snapshot.config.defaultFriction}`);
        this.print(`  researchEnabled: ${snapshot.config.researchEnabled}`);
        this.print(`  logLevel: ${snapshot.config.logLevel}`);
        this.print(`  activeSession: ${snapshot.session?.id ?? 'none'}`);
        this.print(`  phase: ${snapshot.session?.phase ?? 'n/a'}`);
        this.print('Use /set <key> <value> to update: apiUrl, defaultFriction, researchEnabled, logLevel');
        return { kind: 'noop' };
      }
      case 'set':
        await this.controller.setParam(parsed.key, parsed.value);
        this.print(`Updated ${parsed.key}.`);
        return { kind: 'noop' };
      case 'new':
        await this.controller.clearShellSessionSelection();
        this.print('Ready for a new idea.');
        return { kind: 'noop' };
      case 'session': {
        const session = await this.controller.selectSession(parsed.sessionId ?? '');
        this.print(`Using session ${session.id}.`);
        return { kind: 'noop' };
      }
      case 'status': {
        const status = await this.controller.getStatus(parsed.sessionId);
        return {
          kind: 'status',
          status: status.status,
          phase: status.phase,
          sessionId: status.id
        };
      }
      case 'show': {
        const artifact = await this.controller.getArtifact(parsed.artifactType, {
          refresh: parsed.refresh,
          raw: parsed.raw,
          sessionId: undefined
        });
        return {
          kind: 'artifact',
          content: artifact.content,
          raw: artifact.raw,
          sessionId: artifact.sessionId
        };
      }
      case 'follow-up': {
        const followUp = await this.controller.submitFollowUp(parsed.message);
        return {
          kind: 'follow-up',
          response: followUp.responseMessage
            ? {
                agentId: followUp.responseMessage.agentId,
                message: followUp.responseMessage.message
              }
            : undefined
        };
      }
    }
  }
}
