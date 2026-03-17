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
  resumeSession(sessionId?: string): Promise<SessionResponse>;
  listSessions(): Promise<SessionResponse[]>;
  getStatus(sessionId?: string): Promise<SessionResponse>;
  getArtifact(
    type: 'verdict' | 'plan' | 'prd' | 'risks' | 'assumptions' | 'architecture' | 'copilot',
    options: { refresh: boolean; raw: boolean; sessionId?: string }
  ): Promise<{ sessionId: string; content: string; raw: boolean }>;
  submitFollowUp(content: string): Promise<{
    session: SessionResponse;
    responseMessage?: { agentId?: string; message: string };
  }>;
  attachDocument(path: string, explicitSessionId?: string): Promise<{
    sessionId: string;
    attachment: {
      fileName: string;
      contentType: string;
      sizeBytes: number;
      hasExtractedText: boolean;
    };
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
  | { kind: 'notice'; message: string }
  | { kind: 'started'; sessionId: string; response?: { agentId?: string; message: string } }
  | {
      kind: 'follow-up';
      sessionId: string;
      status: string;
      phase: string;
      response?: { agentId?: string; message: string };
    }
  | { kind: 'status'; status: string; phase: string; sessionId: string }
  | {
      kind: 'attachment';
      sessionId: string;
      fileName: string;
      contentType: string;
      sizeBytes: number;
      hasExtractedText: boolean;
    }
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
        sessionId: followUp.session.id,
        status: followUp.session.status,
        phase: followUp.session.phase,
        response: followUp.responseMessage
          ? {
              agentId: followUp.responseMessage.agentId,
              message: followUp.responseMessage.message
            }
          : undefined
      };
    }

    return { kind: 'notice', message: route.reason };
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
        this.print('  /show-sessions                List your sessions');
        this.print('  /resume [session-id]          Resume latest session (or specific session)');
        this.print('  /session <session-id>         Switch active session');
        this.print('  /status [session-id]          Fetch session status/phase');
        this.print('  /show <artifact> [flags]      Show artifact (e.g. verdict, prd)');
        this.print('  /refresh [artifact]           Refresh latest artifact (default: verdict)');
        this.print('  /attach <file-path>           Attach a document/image to the active session');
        this.print('  /follow-up <message>          Send explicit follow-up message');
        this.print('  /exit                         Exit shell (also: /quit)');
        this.print('Examples:');
        this.print('  /set defaultFriction 75');
        this.print('  /set researchEnabled false');
        this.print('  /set logLevel warn');
        this.print('  /set apiUrl http://localhost:5000');
        this.print('  /attach ./docs/product-brief.md');
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
        return { kind: 'notice', message: `Updated ${parsed.key}.` };
      case 'new':
        await this.controller.clearShellSessionSelection();
        this.print('Ready for a new idea.');
        return { kind: 'noop' };
      case 'show-sessions': {
        const sessions = await this.controller.listSessions();
        if (sessions.length === 0) {
          this.print('No sessions found.');
          return { kind: 'noop' };
        }

        this.print('Sessions:');
        for (const [index, session] of sessions.entries()) {
          this.print(
            `  ${index + 1}. ${session.id}  status=${session.status}  phase=${session.phase}  updated=${session.updatedAt}`
          );
        }

        this.print('Use /resume <session-id> to switch session.');
        return { kind: 'noop' };
      }
      case 'resume': {
        const session = await this.controller.resumeSession(parsed.sessionId);
        this.print(`Using session ${session.id}.`);
        return { kind: 'noop' };
      }
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
      case 'refresh': {
        const artifact = await this.controller.getArtifact(parsed.artifactType ?? 'verdict', {
          refresh: true,
          raw: false,
          sessionId: undefined
        });
        return {
          kind: 'artifact',
          content: artifact.content,
          raw: artifact.raw,
          sessionId: artifact.sessionId
        };
      }
      case 'attach': {
        const result = await this.controller.attachDocument(parsed.path);
        return {
          kind: 'attachment',
          sessionId: result.sessionId,
          fileName: result.attachment.fileName,
          contentType: result.attachment.contentType,
          sizeBytes: result.attachment.sizeBytes,
          hasExtractedText: result.attachment.hasExtractedText
        };
      }
      case 'follow-up': {
        const followUp = await this.controller.submitFollowUp(parsed.message);
        return {
          kind: 'follow-up',
          sessionId: followUp.session.id,
          status: followUp.session.status,
          phase: followUp.session.phase,
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
