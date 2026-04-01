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

  it('shrinks prompt frame when backspace reduces wrapped input below a line boundary', async () => {
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
    const { CTRL_C_EXIT_SENTINEL } = shellModule;
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
    const prompt = shell.promptForInput(frame, null);

    // Type enough to cause the frame to grow beyond the minimum 2 lines
    const longInput = 'word '.repeat(frame.maxInputCharsPerLine);
    fakeInput.emit('keypress', longInput, {
      sequence: longInput,
      name: undefined,
      ctrl: false,
      meta: false,
    });

    await waitFor(
      () => /\u001b\[(?:5|6|7|8|9|1\d)A/.test(fakeOutput.writes.join('')),
      3_000,
      'Expected prompt frame growth was not emitted',
    );

    const writesAfterGrowth = fakeOutput.writes.join('').length;

    // Delete all characters — frame should shrink back to 2-line minimum
    for (let i = 0; i < longInput.length; i += 1) {
      fakeInput.emit('keypress', undefined, { name: 'backspace', ctrl: false, meta: false, sequence: '\x7f' });
    }

    await waitFor(
      () => {
        const newWrites = fakeOutput.writes.join('').slice(writesAfterGrowth);
        // After shrinking, a 2-line frame resize emits cursorUpLines = 3 (inputLineCount 2 + 1)
        return /\u001b\[3A/.test(newWrites);
      },
      3_000,
      'Expected prompt frame shrink was not emitted after deleting all input',
    );

    // After deleting all input value is empty — Ctrl+C on empty input exits the shell
    fakeInput.emit('keypress', '\x03', { ctrl: true, name: 'c', sequence: '\x03' });
    const result = await prompt;
    expect(result).toBe(CTRL_C_EXIT_SENTINEL);
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
