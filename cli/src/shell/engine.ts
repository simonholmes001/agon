import { promises as fs } from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import type { SessionResponse } from '../api/types.js';
import { getSelfUpdateRestartNotice, type SelfUpdateFailureCategory } from '../utils/self-update.js';
import { extractImplicitAttach, extractInlineAttach, parseShellInput } from './parser.js';
import { styleAttachmentToken } from './renderer.js';
import type { PlainInputRoute } from './router.js';

interface ShellControllerLike {
  getParamsSnapshot(): Promise<{
    config: {
      apiUrl: string;
      apiUrlSource: 'default' | 'user' | 'admin';
      apiUrlMode: 'managed' | 'custom';
      apiUrlUpgradeSuggestion: string | null;
      defaultFriction: number;
      researchEnabled: boolean;
      logLevel: 'debug' | 'info' | 'warn' | 'error';
    };
    session: SessionResponse | null;
  }>;
  setParam(key: 'apiUrl' | 'defaultFriction' | 'researchEnabled' | 'logLevel', value: string): Promise<void>;
  unsetParam(key: 'apiUrl' | 'defaultFriction' | 'researchEnabled' | 'logLevel'): Promise<void>;
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
  selfUpdate: (options: { check: boolean }) => Promise<ShellSelfUpdateResult>;
  print: (line: string) => void;
}

export type ShellSelfUpdateResult =
  | {
      status: 'up-to-date';
      currentVersion: string;
    }
  | {
      status: 'update-available';
      currentVersion: string;
      latestVersion: string;
      installCommand: string;
    }
  | {
      status: 'updated';
      currentVersion: string;
      latestVersion: string;
    }
  | {
      status: 'failed';
      currentVersion: string;
      latestVersion: string;
      reason: SelfUpdateFailureCategory;
      message: string;
      guidance: string;
      installCommand: string;
    };

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
      referenceLabel: string;
      fileName: string;
      contentType: string;
      sizeBytes: number;
      hasExtractedText: boolean;
    }
  | { kind: 'artifact'; content: string; raw: boolean; sessionId: string };

export class ShellEngine {
  private readonly controller: ShellControllerLike;
  private readonly routePlainInputFn: (session: SessionResponse | null) => PlainInputRoute;
  private readonly selfUpdateFn: (options: { check: boolean }) => Promise<ShellSelfUpdateResult>;
  private readonly print: (line: string) => void;
  private readonly attachmentRefCounters = new Map<string, { image: number; file: number }>();

  constructor(deps: ShellEngineDeps) {
    this.controller = deps.controller;
    this.routePlainInputFn = deps.routePlainInput;
    this.selfUpdateFn = deps.selfUpdate;
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

    return this.handlePlainInput(parsed.text);
  }

  private async handlePlainInput(text: string): Promise<ShellEngineOutcome> {
    const inlineAttach = extractInlineAttach(text);
    if (inlineAttach?.type === 'error') {
      this.print(inlineAttach.message);
      return { kind: 'noop' };
    }

    let plainText = text;
    if (inlineAttach?.type === 'attach') {
      const result = await this.controller.attachDocument(inlineAttach.path);
      const referenceLabel = this.nextAttachmentReferenceLabel(result.sessionId, result.attachment.contentType);
      if (!inlineAttach.remainingText) {
        return {
          kind: 'attachment',
          sessionId: result.sessionId,
          referenceLabel,
          fileName: result.attachment.fileName,
          contentType: result.attachment.contentType,
          sizeBytes: result.attachment.sizeBytes,
          hasExtractedText: result.attachment.hasExtractedText
        };
      }

      this.print(
        `Attached ${styleAttachmentToken(referenceLabel)} (${styleAttachmentToken(result.attachment.fileName)}) to session ${result.sessionId}.`
        + ` Type: ${result.attachment.contentType} | Size: ${result.attachment.sizeBytes} B`
      );
      this.printAttachmentExtractionMessage(result.attachment.contentType, result.attachment.hasExtractedText);

      plainText = composeAttachmentScopedFollowUp(
        inlineAttach.remainingText,
        result.attachment.fileName,
        result.attachment.contentType
      );
    }

    if (!inlineAttach) {
      const implicitAttach = extractImplicitAttach(text);
        if (implicitAttach?.type === 'attach') {
          const resolvedPath = await resolvePathIfExisting(implicitAttach.path);
          if (resolvedPath) {
            const result = await this.controller.attachDocument(resolvedPath);
          const referenceLabel = this.nextAttachmentReferenceLabel(result.sessionId, result.attachment.contentType);
          if (!implicitAttach.remainingText) {
            return {
              kind: 'attachment',
              sessionId: result.sessionId,
              referenceLabel,
              fileName: result.attachment.fileName,
              contentType: result.attachment.contentType,
              sizeBytes: result.attachment.sizeBytes,
              hasExtractedText: result.attachment.hasExtractedText
            };
          }

          this.print(
            `Attached ${styleAttachmentToken(referenceLabel)} (${styleAttachmentToken(result.attachment.fileName)}) to session ${result.sessionId}.`
            + ` Type: ${result.attachment.contentType} | Size: ${result.attachment.sizeBytes} B`
          );
          this.printAttachmentExtractionMessage(result.attachment.contentType, result.attachment.hasExtractedText);

          plainText = composeAttachmentScopedFollowUp(
            implicitAttach.remainingText,
            result.attachment.fileName,
            result.attachment.contentType
          );
        } else {
          this.print(`File not found: ${implicitAttach.path}`);
          this.print('Attachment was not sent. Provide a valid local file path, then retry.');
          return { kind: 'noop' };
        }
      }
    }

    const activeSession = await this.controller.getActiveSession();
    const route = this.routePlainInputFn(activeSession);

    if (route.action === 'start') {
      const started = await this.controller.startIdea(plainText);
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
      const followUp = await this.controller.submitFollowUp(plainText);
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
      case 'help': {
        const commands: Array<{ token: string; description: string }> = [
          { token: '/attach <file-path>',           description: 'Attach a document/image to the active session' },
          { token: '/exit',                          description: 'Exit shell (also: /quit)' },
          { token: '/follow-up <message>',           description: 'Send explicit follow-up message' },
          { token: '/help',                          description: 'Show this command reference' },
          { token: '/new',                           description: 'Reset shell to awaiting-idea mode' },
          { token: '/params',                        description: 'Show current shell parameters and active session' },
          { token: '/refresh [artifact]',            description: 'Refresh latest artifact (default: verdict)' },
          { token: '/resume [session-id]',           description: 'Resume latest session (or specific session)' },
          { token: '/session <session-id>',          description: 'Switch active session' },
          { token: '/update [--check]',               description: 'Update CLI in-session (--check to only verify)' },
          { token: '/set <key> <value>',             description: 'Persist config key (apiUrl|defaultFriction|researchEnabled|logLevel)' },
          { token: '/unset <key>',                   description: 'Remove persisted key and fall back to managed defaults' },
          { token: '/show <artifact> [flags]',       description: 'Show artifact (e.g. verdict, prd)' },
          { token: '/show-sessions',                 description: 'List your sessions' },
          { token: '/status [session-id]',           description: 'Fetch session status/phase' },
        ].sort((a, b) => a.token.localeCompare(b.token));

        this.print('Commands:');
        for (const { token, description } of commands) {
          this.print(`  ${token.padEnd(30)}${description}`);
        }
        this.print('Examples:');
        this.print('  /set defaultFriction 75');
        this.print('  /set researchEnabled false');
        this.print('  /set logLevel warn');
        this.print('  /set apiUrl https://api-dev.agon-agents.org');
        this.print('  /unset apiUrl');
        this.print('  /attach ./docs/product-brief.md');
        this.print('  /Users/simonholmes/Documents/brief.pdf');
        this.print('  /Users/simonholmes/Documents/brief.pdf summarize the risks');
        this.print('Outside shell:');
        this.print('  agon command onboard');
        return { kind: 'noop' };
      }
      case 'params': {
        const snapshot = await this.controller.getParamsSnapshot();
        this.print('Current parameters:');
        this.print(`  apiUrl: ${snapshot.config.apiUrl}`);
        this.print(`  apiUrlSource: ${snapshot.config.apiUrlSource} (${snapshot.config.apiUrlMode})`);
        this.print(`  defaultFriction: ${snapshot.config.defaultFriction} (0-100 debate rigor)`);
        this.print(`  researchEnabled: ${snapshot.config.researchEnabled} (web research tools on/off)`);
        this.print(`  logLevel: ${snapshot.config.logLevel}`);
        this.print(`  activeSession: ${snapshot.session?.id ?? 'none'}`);
        this.print(`  phase: ${snapshot.session?.phase ?? 'n/a'}`);
        if (snapshot.config.apiUrlUpgradeSuggestion) {
          this.print(
            `  tip: /set apiUrl ${snapshot.config.apiUrlUpgradeSuggestion} (detected HTTP -> HTTPS redirect)`
          );
        }
        this.print('Use /set <key> <value> to update: apiUrl, defaultFriction, researchEnabled, logLevel');
        this.print('Use /unset <key> to remove an override and return to managed defaults.');
        return { kind: 'noop' };
      }
      case 'set':
        await this.controller.setParam(parsed.key, parsed.value);
        if (parsed.key === 'apiUrl') {
          this.print('Custom backend URL enabled. Managed endpoint upgrades are disabled until /unset apiUrl.');
        }
        return { kind: 'notice', message: `Updated ${parsed.key}.` };
      case 'unset':
        await this.controller.unsetParam(parsed.key);
        if (parsed.key === 'apiUrl') {
          return { kind: 'notice', message: 'Cleared apiUrl override. Reverted to managed backend URL.' };
        }
        return { kind: 'notice', message: `Cleared ${parsed.key}.` };
      case 'new':
        await this.controller.clearShellSessionSelection();
        this.print('Ready for a new idea.');
        return { kind: 'noop' };
      case 'update': {
        const result = await this.selfUpdateFn({ check: parsed.check });
        switch (result.status) {
          case 'up-to-date':
            return { kind: 'notice', message: `You are already on the latest version (${result.currentVersion}).` };
          case 'update-available':
            this.print(`Update available: v${result.currentVersion} -> v${result.latestVersion}`);
            this.print(`Install with: ${result.installCommand}`);
            return { kind: 'noop' };
          case 'updated':
            this.print(`Updated CLI from v${result.currentVersion} to v${result.latestVersion}.`);
            this.print(getSelfUpdateRestartNotice(result.latestVersion));
            return { kind: 'noop' };
          case 'failed':
            this.print(`Self-update failed (${result.reason}): ${result.message}`);
            this.print(result.guidance);
            return { kind: 'noop' };
        }
      }
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
        const referenceLabel = this.nextAttachmentReferenceLabel(result.sessionId, result.attachment.contentType);
        return {
          kind: 'attachment',
          sessionId: result.sessionId,
          referenceLabel,
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

  private printAttachmentExtractionMessage(contentType: string, hasExtractedText: boolean): void {
    if (hasExtractedText) {
      this.print('Attachment content extracted and added to agent context.');
      return;
    }

    if (contentType.toLowerCase().startsWith('image/')) {
      this.print(
        'Image uploaded, but backend vision extraction returned no content. '
        + 'Verify backend OpenAI vision settings (OPENAI_KEY and ATTACHMENTPROCESSING__OPENAIVISION__ENABLED).'
      );
      return;
    }

    this.print('No text extraction available; file metadata/link still added to context.');
  }

  private nextAttachmentReferenceLabel(sessionId: string, contentType: string): string {
    const counters = this.attachmentRefCounters.get(sessionId) ?? { image: 0, file: 0 };
    const isImage = contentType.toLowerCase().startsWith('image/');
    if (isImage) {
      counters.image += 1;
      this.attachmentRefCounters.set(sessionId, counters);
      return `[Image #${counters.image}]`;
    }

    counters.file += 1;
    this.attachmentRefCounters.set(sessionId, counters);
    return `[File #${counters.file}]`;
  }
}

async function resolvePathIfExisting(inputPath: string): Promise<string | null> {
  const expandedPath = expandTilde(inputPath.trim());
  if (!expandedPath) {
    return null;
  }

  const absolutePath = path.isAbsolute(expandedPath)
    ? expandedPath
    : path.resolve(expandedPath);

  try {
    const stat = await fs.stat(absolutePath);
    if (!stat.isFile()) {
      return null;
    }

    return absolutePath;
  } catch {
    return null;
  }
}

function composeAttachmentScopedFollowUp(
  remainingText: string,
  fileName: string,
  contentType: string
): string {
  const normalizedMessage = remainingText.trim();
  const fileKind = contentType.toLowerCase().startsWith('image/') ? 'image' : 'document';
  return `${normalizedMessage}\n\nFocus on the newly attached ${fileKind}: ${fileName}.`;
}

function expandTilde(value: string): string {
  if (!value.startsWith('~/')) {
    return value;
  }

  return path.join(os.homedir(), value.slice(2));
}
