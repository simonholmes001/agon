import { promises as fs } from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { SessionResponse } from '../../src/api/types.js';
import { ShellEngine } from '../../src/shell/engine.js';

describe('shell engine', () => {
  let controller: any;
  let selfUpdate: ReturnType<typeof vi.fn>;
  let print: ReturnType<typeof vi.fn>;
  let routeFn: ReturnType<typeof vi.fn>;
  let engine: ShellEngine;
  let tempDirs: string[];

  const session: SessionResponse = {
    id: 'session-123',
    status: 'active',
    phase: 'Clarification',
    createdAt: '2026-03-10T10:00:00Z',
    updatedAt: '2026-03-10T10:00:00Z'
  };

  beforeEach(() => {
    tempDirs = [];
    controller = {
      getParamsSnapshot: vi.fn().mockResolvedValue({
        config: {
          apiUrl: 'http://localhost:5000',
          apiUrlSource: 'user',
          apiUrlMode: 'custom',
          apiUrlUpgradeSuggestion: 'https://localhost:5000/',
          defaultFriction: 50,
          researchEnabled: true,
          logLevel: 'info'
        },
        session
      }),
      setParam: vi.fn(),
      unsetParam: vi.fn(),
      clearShellSessionSelection: vi.fn(),
      selectSession: vi.fn().mockResolvedValue(session),
      resumeSession: vi.fn().mockResolvedValue(session),
      listSessions: vi.fn().mockResolvedValue([session]),
      getStatus: vi.fn().mockResolvedValue(session),
      getArtifact: vi.fn().mockResolvedValue({
        sessionId: 'session-123',
        content: '# Verdict',
        raw: false
      }),
      attachDocument: vi.fn().mockResolvedValue({
        sessionId: 'session-123',
        attachment: {
          fileName: 'spec.md',
          contentType: 'text/markdown',
          sizeBytes: 1024,
          hasExtractedText: true
        }
      }),
      submitFollowUp: vi.fn().mockResolvedValue({
        session,
        responseMessage: {
          agentId: 'moderator',
          message: 'Next question',
          round: 1,
          createdAt: '2026-03-10T10:01:00Z'
        }
      }),
      startIdea: vi.fn().mockResolvedValue({
        session,
        responseMessage: {
          agentId: 'moderator',
          message: 'Please clarify your target audience.',
          round: 0,
          createdAt: '2026-03-10T10:00:30Z'
        }
      }),
      getActiveSession: vi.fn().mockResolvedValue(session)
    };

    selfUpdate = vi.fn().mockResolvedValue({
      status: 'up-to-date',
      currentVersion: '0.1.10'
    });
    print = vi.fn();
    routeFn = vi.fn().mockReturnValue({ action: 'follow-up' });

    engine = new ShellEngine({
      controller,
      routePlainInput: routeFn,
      selfUpdate,
      print
    });
  });

  afterEach(async () => {
    await Promise.all(
      tempDirs.map((tempDir) => fs.rm(tempDir, { recursive: true, force: true }))
    );
  });

  it('executes /set via controller', async () => {
    const outcome = await engine.handleInput('/set defaultFriction 75');
    expect(controller.setParam).toHaveBeenCalledWith('defaultFriction', '75');
    expect(outcome).toEqual({ kind: 'notice', message: 'Updated defaultFriction.' });
  });

  it('executes /unset via controller', async () => {
    const outcome = await engine.handleInput('/unset apiUrl');
    expect(controller.unsetParam).toHaveBeenCalledWith('apiUrl');
    expect(outcome).toEqual({ kind: 'notice', message: 'Cleared apiUrl override. Reverted to managed backend URL.' });
  });

  it('prints detailed command help for /help', async () => {
    await engine.handleInput('/help');
    expect(print).toHaveBeenCalledWith('Commands:');
    expect(print).toHaveBeenCalledWith(expect.stringContaining('/set <key> <value>'));
    expect(print).toHaveBeenCalledWith(expect.stringContaining('/unset <key>'));
    expect(print).toHaveBeenCalledWith(expect.stringContaining('/show-sessions'));
    expect(print).toHaveBeenCalledWith(expect.stringContaining('/resume [session-id]'));
    expect(print).toHaveBeenCalledWith(expect.stringContaining('/refresh [artifact]'));
    expect(print).toHaveBeenCalledWith(expect.stringContaining('/attach <file-path>'));
    expect(print).toHaveBeenCalledWith(expect.stringContaining('/exit'));
    expect(print).toHaveBeenCalledWith(expect.stringContaining('/update'));
    expect(print).toHaveBeenCalledWith('  /set defaultFriction 75');
    expect(print).toHaveBeenCalledWith('  /set apiUrl https://api-dev.agon-agents.org');
    expect(print).toHaveBeenCalledWith('  /unset apiUrl');
  });

  it('prints /help command entries in strict alphabetical order', async () => {
    await engine.handleInput('/help');

    const allLines: string[] = print.mock.calls.map((callArgs: string[]) => callArgs[0]);

    // Collect only the command definition lines (between 'Commands:' and 'Examples:' headers)
    const commandsStart = allLines.indexOf('Commands:');
    const examplesStart = allLines.indexOf('Examples:');
    // Matches indented slash-command entries, e.g. "  /attach <file-path>  ..."
    const isCommandEntry = (line: string): boolean => /^\s+\//.test(line);
    const commandLines = allLines
      .slice(commandsStart + 1, examplesStart)
      .filter(isCommandEntry);

    // Extract the slash-token (first word after leading whitespace)
    const tokens = commandLines.map((line: string) => line.trimStart().split(/\s+/)[0]);

    const sorted = [...tokens].sort((a: string, b: string) => a.localeCompare(b));
    expect(tokens).toEqual(sorted);
  });

  it('executes /show-sessions via controller', async () => {
    await engine.handleInput('/show-sessions');
    expect(controller.listSessions).toHaveBeenCalled();
    expect(print).toHaveBeenCalledWith('Sessions:');
  });

  it('executes /resume with optional session id', async () => {
    await engine.handleInput('/resume');
    expect(controller.resumeSession).toHaveBeenCalledWith(undefined);

    await engine.handleInput('/resume session-123');
    expect(controller.resumeSession).toHaveBeenCalledWith('session-123');
  });

  it('executes /status via controller', async () => {
    await engine.handleInput('/status');
    expect(controller.getStatus).toHaveBeenCalled();
  });

  it('prints full parameter snapshot for /params', async () => {
    await engine.handleInput('/params');
    expect(print).toHaveBeenCalledWith('Current parameters:');
    expect(print).toHaveBeenCalledWith('  apiUrlSource: user (custom)');
    expect(print).toHaveBeenCalledWith('  defaultFriction: 50 (0-100 debate rigor)');
    expect(print).toHaveBeenCalledWith('  researchEnabled: true (web research tools on/off)');
    expect(print).toHaveBeenCalledWith('  tip: /set apiUrl https://localhost:5000/ (detected HTTP -> HTTPS redirect)');
    expect(print).toHaveBeenCalledWith('  activeSession: session-123');
    expect(print).toHaveBeenCalledWith('Use /set <key> <value> to update: apiUrl, defaultFriction, researchEnabled, logLevel');
    expect(print).toHaveBeenCalledWith('Use /unset <key> to remove an override and return to managed defaults.');
  });

  it('executes /show with parsed flags', async () => {
    await engine.handleInput('/show verdict --refresh --raw');
    expect(controller.getArtifact).toHaveBeenCalledWith('verdict', {
      refresh: true,
      raw: true,
      sessionId: undefined
    });
  });

  it('executes /refresh with default and explicit artifact', async () => {
    await engine.handleInput('/refresh');
    expect(controller.getArtifact).toHaveBeenCalledWith('verdict', {
      refresh: true,
      raw: false,
      sessionId: undefined
    });

    await engine.handleInput('/refresh prd');
    expect(controller.getArtifact).toHaveBeenCalledWith('prd', {
      refresh: true,
      raw: false,
      sessionId: undefined
    });
  });

  it('routes plain input to start when router says start', async () => {
    routeFn.mockReturnValue({ action: 'start' });

    const outcome = await engine.handleInput('Build a shell-first UX');

    expect(controller.startIdea).toHaveBeenCalledWith('Build a shell-first UX');
    expect(controller.submitFollowUp).not.toHaveBeenCalled();
    expect(outcome).toEqual({
      kind: 'started',
      sessionId: 'session-123',
      response: {
        agentId: 'moderator',
        message: 'Please clarify your target audience.'
      }
    });
  });

  it('routes plain input to follow-up when router says follow-up', async () => {
    routeFn.mockReturnValue({ action: 'follow-up' });

    await engine.handleInput('Please revise section 2');

    expect(controller.submitFollowUp).toHaveBeenCalledWith('Please revise section 2');
  });

  it('prints guidance and does not submit when router blocks input', async () => {
    routeFn.mockReturnValue({
      action: 'blocked',
      reason: 'Debate is still in progress.'
    });

    const outcome = await engine.handleInput('Any update?');

    expect(controller.submitFollowUp).not.toHaveBeenCalled();
    expect(controller.startIdea).not.toHaveBeenCalled();
    expect(outcome).toEqual({ kind: 'notice', message: 'Debate is still in progress.' });
    expect(print).not.toHaveBeenCalledWith('Debate is still in progress.');
  });

  it('supports explicit /follow-up command', async () => {
    await engine.handleInput('/follow-up revise this');
    expect(controller.submitFollowUp).toHaveBeenCalledWith('revise this');
  });

  it('supports explicit /attach command', async () => {
    const outcome = await engine.handleInput('/attach ./docs/spec.md');
    expect(controller.attachDocument).toHaveBeenCalledWith('./docs/spec.md');
    expect(outcome).toEqual({
      kind: 'attachment',
      sessionId: 'session-123',
      referenceLabel: '[File #1]',
      fileName: 'spec.md',
      contentType: 'text/markdown',
      sizeBytes: 1024,
      hasExtractedText: true
    });
  });

  it('auto-attaches when plain input is an existing local file path', async () => {
    const tempDir = await fs.mkdtemp(path.join(os.tmpdir(), 'agon-shell-engine-'));
    tempDirs.push(tempDir);
    const filePath = path.join(tempDir, 'brief.md');
    await fs.writeFile(filePath, '# brief', 'utf-8');

    const outcome = await engine.handleInput(filePath);

    expect(controller.attachDocument).toHaveBeenCalledWith(filePath);
    expect(controller.startIdea).not.toHaveBeenCalled();
    expect(controller.submitFollowUp).not.toHaveBeenCalled();
    expect(outcome).toEqual({
      kind: 'attachment',
      sessionId: 'session-123',
      referenceLabel: '[File #1]',
      fileName: 'spec.md',
      contentType: 'text/markdown',
      sizeBytes: 1024,
      hasExtractedText: true
    });
  });

  it('auto-attaches escaped-space file paths and submits remaining text', async () => {
    const tempDir = await fs.mkdtemp(path.join(os.tmpdir(), 'agon-shell-engine-'));
    tempDirs.push(tempDir);
    const filePath = path.join(tempDir, 'brief v2.md');
    await fs.writeFile(filePath, '# brief', 'utf-8');
    const escapedPath = filePath.replace(/ /g, '\\ ');

    const outcome = await engine.handleInput(`${escapedPath} summarize key risks`);

    expect(controller.attachDocument).toHaveBeenCalledWith(filePath);
    expect(controller.submitFollowUp).toHaveBeenCalledWith('summarize key risks');
    expect(outcome).toEqual({
      kind: 'follow-up',
      sessionId: 'session-123',
      status: 'active',
      phase: 'Clarification',
      response: {
        agentId: 'moderator',
        message: 'Next question'
      }
    });
  });

  it('auto-attaches when drag-drop path appears after prompt text', async () => {
    const tempDir = await fs.mkdtemp(path.join(os.tmpdir(), 'agon-shell-engine-'));
    tempDirs.push(tempDir);
    const filePath = path.join(tempDir, 'image 01.jpeg');
    await fs.writeFile(filePath, 'fake', 'utf-8');
    const escapedPath = filePath.replace(/ /g, '\\ ');

    const outcome = await engine.handleInput(`Describe this image -> ${escapedPath}`);

    expect(controller.attachDocument).toHaveBeenCalledWith(filePath);
    expect(controller.submitFollowUp).toHaveBeenCalledWith('Describe this image');
    expect(outcome).toEqual({
      kind: 'follow-up',
      sessionId: 'session-123',
      status: 'active',
      phase: 'Clarification',
      response: {
        agentId: 'moderator',
        message: 'Next question'
      }
    });
  });

  it('returns a friendly message when a path-like input file does not exist', async () => {
    const outcome = await engine.handleInput('/tmp/agon-shell-engine-missing-file.pdf');

    expect(outcome).toEqual({ kind: 'noop' });
    expect(controller.attachDocument).not.toHaveBeenCalled();
    expect(controller.startIdea).not.toHaveBeenCalled();
    expect(controller.submitFollowUp).not.toHaveBeenCalled();
    expect(print).toHaveBeenCalledWith('File not found: /tmp/agon-shell-engine-missing-file.pdf');
  });

  it('runs /update --check through updater callback', async () => {
    selfUpdate.mockResolvedValueOnce({
      status: 'update-available',
      currentVersion: '0.1.10',
      latestVersion: '0.1.11',
      installCommand: 'npm install -g @agon_agents/cli@latest'
    });

    const outcome = await engine.handleInput('/update --check');

    expect(selfUpdate).toHaveBeenCalledWith({ check: true });
    expect(outcome).toEqual({ kind: 'noop' });
    expect(print).toHaveBeenCalledWith('Update available: v0.1.10 -> v0.1.11');
  });

  it('prints explicit restart guidance after /update installs a new version', async () => {
    selfUpdate.mockResolvedValueOnce({
      status: 'updated',
      currentVersion: '0.5.1',
      latestVersion: '0.6.0'
    });

    const outcome = await engine.handleInput('/update');

    expect(outcome).toEqual({ kind: 'noop' });
    expect(print).toHaveBeenCalledWith('Updated CLI from v0.5.1 to v0.6.0.');
    expect(print).toHaveBeenCalledWith(
      'Update installed, but this shell is still running the previous runtime. Exit now and restart Agon to use v0.6.0.'
    );
  });

  it('assigns codex-style attachment labels per session for images', async () => {
    controller.attachDocument.mockResolvedValueOnce({
      sessionId: 'session-123',
      attachment: {
        fileName: 'image-one.jpeg',
        contentType: 'image/jpeg',
        sizeBytes: 2048,
        hasExtractedText: true
      }
    });
    controller.attachDocument.mockResolvedValueOnce({
      sessionId: 'session-123',
      attachment: {
        fileName: 'image-two.jpeg',
        contentType: 'image/jpeg',
        sizeBytes: 4096,
        hasExtractedText: true
      }
    });

    const first = await engine.handleInput('/attach /tmp/image-one.jpeg');
    const second = await engine.handleInput('/attach /tmp/image-two.jpeg');

    expect(first).toEqual({
      kind: 'attachment',
      sessionId: 'session-123',
      referenceLabel: '[Image #1]',
      fileName: 'image-one.jpeg',
      contentType: 'image/jpeg',
      sizeBytes: 2048,
      hasExtractedText: true
    });
    expect(second).toEqual({
      kind: 'attachment',
      sessionId: 'session-123',
      referenceLabel: '[Image #2]',
      fileName: 'image-two.jpeg',
      contentType: 'image/jpeg',
      sizeBytes: 4096,
      hasExtractedText: true
    });
  });
});
