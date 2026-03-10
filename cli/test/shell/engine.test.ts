import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SessionResponse } from '../../src/api/types.js';
import { ShellEngine } from '../../src/shell/engine.js';

describe('shell engine', () => {
  let controller: any;
  let print: ReturnType<typeof vi.fn>;
  let routeFn: ReturnType<typeof vi.fn>;
  let engine: ShellEngine;

  const session: SessionResponse = {
    id: 'session-123',
    status: 'active',
    phase: 'Clarification',
    createdAt: '2026-03-10T10:00:00Z',
    updatedAt: '2026-03-10T10:00:00Z'
  };

  beforeEach(() => {
    controller = {
      getParamsSnapshot: vi.fn().mockResolvedValue({
        config: {
          apiUrl: 'http://localhost:5000',
          defaultFriction: 50,
          researchEnabled: true,
          logLevel: 'info'
        },
        session
      }),
      setParam: vi.fn(),
      clearShellSessionSelection: vi.fn(),
      selectSession: vi.fn().mockResolvedValue(session),
      getStatus: vi.fn().mockResolvedValue(session),
      getArtifact: vi.fn().mockResolvedValue({
        sessionId: 'session-123',
        content: '# Verdict',
        raw: false
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

    print = vi.fn();
    routeFn = vi.fn().mockReturnValue({ action: 'follow-up' });

    engine = new ShellEngine({
      controller,
      routePlainInput: routeFn,
      print
    });
  });

  it('executes /set via controller', async () => {
    const outcome = await engine.handleInput('/set defaultFriction 75');
    expect(controller.setParam).toHaveBeenCalledWith('defaultFriction', '75');
    expect(outcome).toEqual({ kind: 'notice', message: 'Updated defaultFriction.' });
  });

  it('prints detailed command help for /help', async () => {
    await engine.handleInput('/help');
    expect(print).toHaveBeenCalledWith('Commands:');
    expect(print).toHaveBeenCalledWith('  /set <key> <value>            Persist config key (apiUrl|defaultFriction|researchEnabled|logLevel)');
    expect(print).toHaveBeenCalledWith('  /set defaultFriction 75');
  });

  it('executes /status via controller', async () => {
    await engine.handleInput('/status');
    expect(controller.getStatus).toHaveBeenCalled();
  });

  it('prints full parameter snapshot for /params', async () => {
    await engine.handleInput('/params');
    expect(print).toHaveBeenCalledWith('Current parameters:');
    expect(print).toHaveBeenCalledWith('  activeSession: session-123');
    expect(print).toHaveBeenCalledWith('Use /set <key> <value> to update: apiUrl, defaultFriction, researchEnabled, logLevel');
  });

  it('executes /show with parsed flags', async () => {
    await engine.handleInput('/show verdict --refresh --raw');
    expect(controller.getArtifact).toHaveBeenCalledWith('verdict', {
      refresh: true,
      raw: true,
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
});
