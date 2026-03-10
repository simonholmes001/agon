import { Command } from '@oclif/core';
import ora from 'ora';
import chalk from 'chalk';
import { emitKeypressEvents, type Key } from 'node:readline';
import { stdin as input, stdout as output } from 'node:process';
import { AgonAPIClient } from '../api/agon-client.js';
import { ConfigManager } from '../state/config-manager.js';
import { SessionManager } from '../state/session-manager.js';
import { ShellController } from '../shell/controller.js';
import { ShellEngine, type ShellEngineOutcome } from '../shell/engine.js';
import { parseShellInput } from '../shell/parser.js';
import { routePlainInput } from '../shell/router.js';
import {
  buildActivePrompt,
  buildShimmerText,
  buildPromptInputLine,
  type PromptFrameContext,
  renderMessagePanel,
  renderPromptBanner,
  renderShellHeader,
  renderStatusLine
} from '../shell/renderer.js';

export default class Shell extends Command {
  static override readonly description = 'Open interactive codex-style Agon shell';

  static override readonly examples = [
    '<%= config.bin %>',
    '<%= config.bin %> shell'
  ];

  private spinnerActive = false;
  private pendingPrintLines: string[] = [];
  private inRawInputMode = false;

  public async run(): Promise<void> {
    const configManager = new ConfigManager();
    const sessionManager = new SessionManager();
    const config = await configManager.load();
    const apiClient = new AgonAPIClient(config.apiUrl);

    const controller = new ShellController({
      apiClient,
      sessionManager,
      configManager
    });

    const engine = new ShellEngine({
      controller,
      routePlainInput,
      print: (line: string) => this.printLine(line)
    });

    const snapshot = await controller.getParamsSnapshot();
    renderShellHeader(
      {
        version: this.config.pjson.version ?? 'dev',
        modelLabel: 'OpenAI GPT (backend configured)',
        directory: process.cwd(),
        config: snapshot.config,
        session: snapshot.session
      },
      (line) => this.log(line)
    );
    renderStatusLine((line) => this.log(line));
    this.log('');

    try {
      while (true) {
        const promptFrame = renderPromptBanner((line) => this.log(line));
        const rawInput = await this.promptForInput(promptFrame);

        const trimmed = rawInput.trim();
        if (trimmed === '/exit' || trimmed === '/quit') {
          this.log(chalk.dim('Exiting shell.'));
          return;
        }

        const parsed = parseShellInput(trimmed);
        const spinnerText = getSpinnerText(parsed);
        const spinner = spinnerText
          ? ora({ text: spinnerText, color: 'cyan' }).start()
          : null;
        const stopShimmer = spinner && spinnerText
          ? this.startSpinnerShimmer(spinner, spinnerText)
          : null;
        this.spinnerActive = spinner !== null;

        try {
          const outcome = await engine.handleInput(trimmed);
          stopShimmer?.();
          if (spinner) {
            spinner.succeed('Done');
          }
          this.spinnerActive = false;
          this.flushPendingLines();
          this.renderOutcome(outcome);
        } catch (error) {
          stopShimmer?.();
          if (spinner) {
            spinner.fail('Failed');
          }
          this.spinnerActive = false;
          this.flushPendingLines();
          const message = error instanceof Error ? error.message : String(error);
          this.log(chalk.red(`Error: ${message}`));
        }

        this.log('');
      }
    } finally {
      this.restoreTerminalState();
    }
  }

  private renderOutcome(outcome: ShellEngineOutcome): void {
    switch (outcome.kind) {
      case 'notice':
        this.log(outcome.message);
        return;
      case 'started':
        this.log(chalk.green(`✓ Session started: ${outcome.sessionId}`));
        if (outcome.response?.message) {
          const title = outcome.response.agentId === 'moderator' ? 'Moderator' : 'Assistant';
          const color = title === 'Moderator' ? 'cyan' : 'green';
          renderMessagePanel(title, outcome.response.message, color, (line) => this.log(line));
        } else {
          this.log(chalk.yellow('No response yet. Run /status or send your next input when ready.'));
        }
        return;
      case 'follow-up':
        if (outcome.response?.message) {
          const title = outcome.response.agentId === 'moderator' ? 'Moderator' : 'Assistant';
          const color = title === 'Moderator' ? 'cyan' : 'green';
          renderMessagePanel(title, outcome.response.message, color, (line) => this.log(line));
        } else {
          this.log(chalk.yellow('No new assistant/moderator response yet.'));
        }
        return;
      case 'status':
        this.log(`Session ${outcome.sessionId} | status=${outcome.status} | phase=${outcome.phase}`);
        return;
      case 'artifact':
        if (outcome.raw) {
          this.log(outcome.content);
        } else {
          renderMessagePanel(`Artifact (${outcome.sessionId})`, outcome.content, 'cyan', (line) => this.log(line));
        }
        return;
      case 'noop':
        return;
    }
  }

  private printLine(line: string): void {
    if (this.spinnerActive) {
      this.pendingPrintLines.push(line);
      return;
    }

    this.log(line);
  }

  private flushPendingLines(): void {
    if (this.pendingPrintLines.length === 0) {
      return;
    }

    for (const line of this.pendingPrintLines) {
      this.log(line);
    }

    this.pendingPrintLines = [];
  }

  private startSpinnerShimmer(spinner: { text: string }, baseText: string): () => void {
    let tick = 0;
    spinner.text = buildShimmerText(baseText, tick);

    const interval = setInterval(() => {
      tick += 1;
      spinner.text = buildShimmerText(baseText, tick);
    }, 90);

    if (typeof interval.unref === 'function') {
      interval.unref();
    }

    return () => clearInterval(interval);
  }

  private async promptForInput(
    frame: PromptFrameContext
  ): Promise<string> {
    if (!input.isTTY || typeof input.setRawMode !== 'function') {
      this.log(chalk.red('Interactive shell input requires a TTY.'));
      return '/quit';
    }

    emitKeypressEvents(input);
    input.setRawMode(true);
    input.resume();
    this.inRawInputMode = true;

    output.write(`\u001b[${frame.cursorUpLines}A\r`);
    output.write(buildActivePrompt(frame));

    return await new Promise<string>((resolve) => {
      let value = '';

      const redraw = (): void => {
        output.write('\r');
        output.write(buildPromptInputLine(frame, value));
      };

      const finish = (result: string): void => {
        input.off('keypress', onKeypress);
        input.setRawMode(false);
        this.inRawInputMode = false;
        output.write(`${frame.reset}\u001b[${frame.cursorDownLines}B\r`);
        resolve(result);
      };

      const onKeypress = (str: string, key?: Key): void => {
        const keyName = key?.name;
        const isCtrl = Boolean(key?.ctrl);
        const isMeta = Boolean(key?.meta);
        const keySequence = key?.sequence;

        if (isCtrl && keyName === 'c') {
          finish('/quit');
          return;
        }

        const isEnter = keyName === 'return'
          || keyName === 'enter'
          || str === '\r'
          || str === '\n'
          || keySequence === '\r'
          || keySequence === '\n';

        if (isEnter) {
          if (value.trim().length === 0) {
            output.write('\u0007');
            return;
          }
          finish(value);
          return;
        }

        if (keyName === 'backspace' || keyName === 'delete') {
          if (value.length > 0) {
            value = value.slice(0, -1);
            redraw();
          }
          return;
        }

        if (keyName === 'escape' || keyName === 'tab') {
          return;
        }

        if (str && !isCtrl && !isMeta && str !== '\r' && str !== '\n') {
          if (value.length < frame.maxInputChars) {
            value += str;
            redraw();
          } else {
            output.write('\u0007');
          }
        }
      };

      input.on('keypress', onKeypress);
    });
  }

  private restoreTerminalState(): void {
    if (this.inRawInputMode && input.isTTY && typeof input.setRawMode === 'function') {
      input.setRawMode(false);
      this.inRawInputMode = false;
    }

    output.write('\u001b[0m\r\n');
  }
}

function getSpinnerText(parsed: ReturnType<typeof parseShellInput>): string | null {
  if (parsed.type === 'plain') {
    return 'Processing input...';
  }

  if (parsed.type === 'error') {
    return null;
  }

  switch (parsed.command) {
    case 'help':
    case 'params':
    case 'new':
      return null;
    case 'set':
      return 'Saving parameter...';
    case 'session':
      return 'Switching session...';
    case 'status':
      return 'Fetching status...';
    case 'show':
      return 'Fetching artifact...';
    case 'follow-up':
      return 'Submitting follow-up...';
  }
}
