import { EventEmitter } from 'node:events';
import { afterEach, describe, expect, it, vi } from 'vitest';

class FakeTTYInput extends EventEmitter {
  isTTY = true;
  setRawMode = vi.fn();
  resume = vi.fn();
  pause = vi.fn();
}

class FakeTTYOutput {
  rows = 32;
  columns = 100;
  writes: string[] = [];

  write(value: string): boolean {
    this.writes.push(value);
    return true;
  }
}

async function waitFor(
  predicate: () => boolean,
  timeoutMs: number,
  message: string
): Promise<void> {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (predicate()) {
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error(message);
}

describe('shell prompt TTY integration', () => {
  afterEach(() => {
    vi.resetModules();
    vi.doUnmock('node:process');
  });

  it('grows prompt frame for wrapped input and returns to compact frame on next prompt', async () => {
    const fakeInput = new FakeTTYInput();
    const fakeOutput = new FakeTTYOutput();

    vi.doMock('node:process', () => ({
      stdin: fakeInput,
      stdout: fakeOutput,
      env: process.env,
    }));

    const [
      shellModule,
      rendererModule,
      historyModule,
    ] = await Promise.all([
      import('../../src/commands/shell.js'),
      import('../../src/shell/renderer.js'),
      import('../../src/shell/history.js'),
    ]);

    const Shell = shellModule.default;
    const { INTERRUPT_SENTINEL, CTRL_C_EXIT_SENTINEL } = shellModule;
    const { renderPromptBanner } = rendererModule;
    const { PromptHistory } = historyModule;

    const shell = Object.create(Shell.prototype) as {
      initializeKeypressEvents: () => void;
      inRawInputMode: boolean;
      promptHistory: InstanceType<typeof PromptHistory>;
      livePreviewNextBySession: Map<string, { image: number; file: number }>;
      promptForInput: (frame: any, activeSessionId: string | null) => Promise<string>;
    };

    shell.initializeKeypressEvents = () => {};
    shell.inRawInputMode = false;
    shell.promptHistory = new PromptHistory();
    shell.livePreviewNextBySession = new Map();

    const frame = renderPromptBanner((line: string) => fakeOutput.write(`${line}\n`));
    const firstPrompt = shell.promptForInput(frame, null);

    const longInput = 'wrapped prompt content '.repeat(frame.maxInputCharsPerLine);
    fakeInput.emit('keypress', longInput, {
      sequence: longInput,
      name: undefined,
      ctrl: false,
      meta: false,
    });

    await waitFor(
      () => /\u001b\[(?:5|6|7|8|9|1\d)A/.test(fakeOutput.writes.join('')),
      3_000,
      'Expected prompt frame growth sequence was not emitted',
    );

    fakeInput.emit('keypress', '\x03', { ctrl: true, name: 'c', sequence: '\x03' });
    const firstResult = await firstPrompt;

    const secondPromptStartOffset = fakeOutput.writes.join('').length;
    const secondFrame = renderPromptBanner((line: string) => fakeOutput.write(`${line}\n`));
    const secondPrompt = shell.promptForInput(secondFrame, null);
    fakeInput.emit('keypress', '\x03', { ctrl: true, name: 'c', sequence: '\x03' });
    const secondResult = await secondPrompt;

    const transcript = fakeOutput.writes.join('');
    const secondPromptTranscript = transcript.slice(secondPromptStartOffset);
    const growthIndex = transcript.search(/\u001b\[(?:5|6|7|8|9|1\d)A/);
    const compactFrameSequence = `\u001b[${secondFrame.cursorUpLines}A`;

    expect(firstResult).toBe(INTERRUPT_SENTINEL);
    expect(secondResult).toBe(CTRL_C_EXIT_SENTINEL);
    expect(growthIndex).toBeGreaterThanOrEqual(0);
    expect(secondPromptTranscript).toContain(compactFrameSequence);
    expect(fakeInput.setRawMode).toHaveBeenCalledWith(true);
    expect(fakeInput.setRawMode).toHaveBeenCalledWith(false);
  });
});
