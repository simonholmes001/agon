import { Command } from '@oclif/core';
import ora from 'ora';
import chalk from 'chalk';
import { type Key } from 'node:readline';
import { stdin as input, stdout as output } from 'node:process';
import { AgonAPIClient } from '../api/agon-client.js';
import { ConfigManager } from '../state/config-manager.js';
import { SessionManager } from '../state/session-manager.js';
import { ShellController } from '../shell/controller.js';
import { createKeypressInitializer } from '../shell/keypress.js';
import {
  ShellEngine,
  type ShellEngineOutcome,
  type ShellSelfUpdateResult
} from '../shell/engine.js';
import { parseShellInput } from '../shell/parser.js';
import { routePlainInput } from '../shell/router.js';
import {
  buildActivePrompt,
  buildShimmerText,
  buildPromptInputLineWithCursor,
  getPromptCursorPosition,
  type PromptFrameContext,
  renderMessagePanel,
  renderPromptBanner,
  renderShellHeader,
  renderStatusLine
} from '../shell/renderer.js';
import { PromptHistory } from '../shell/history.js';
import { renderMarkdown } from '../utils/markdown.js';
import { normalizeStatus } from '../utils/session-flow.js';
import { checkForCliUpdate } from '../utils/update-check.js';
import {
  describeSelfUpdateFailure,
  getSelfUpdateGuidance,
  runNpmGlobalInstall
} from '../utils/self-update.js';

/**
 * Sentinel value returned from promptForInput when the user presses Ctrl+C.
 * The main loop treats this as "interrupt current operation and stay in shell"
 * rather than exiting the session.
 */
export const INTERRUPT_SENTINEL = '\x03';

/**
 * Races a promise against an AbortSignal. If the signal fires before the
 * promise settles, an AbortError is thrown.
 */
export function raceAbort<T>(promise: Promise<T>, signal: AbortSignal): Promise<T> {
  if (signal.aborted) {
    return Promise.reject(new DOMException('Aborted', 'AbortError'));
  }

  return new Promise<T>((resolve, reject) => {
    // Guard against double-settlement: once one side wins, the other is a no-op.
    let settled = false;

    const onAbort = (): void => {
      if (settled) return;
      settled = true;
      reject(new DOMException('Aborted', 'AbortError'));
    };

    signal.addEventListener('abort', onAbort, { once: true });

    promise.then(
      (value) => {
        if (settled) return;
        settled = true;
        signal.removeEventListener('abort', onAbort);
        resolve(value);
      },
      (error: unknown) => {
        if (settled) return;
        settled = true;
        signal.removeEventListener('abort', onAbort);
        reject(error);
      }
    );
  });
}

/** Returns true when an error is an AbortError (Ctrl+C interrupt). */
export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

export default class Shell extends Command {
  static override readonly description = 'Open interactive codex-style Agon shell';

  static override readonly examples = [
    '<%= config.bin %>',
    '<%= config.bin %> shell'
  ];

  private spinnerActive = false;
  private pendingPrintLines: string[] = [];
  private inRawInputMode = false;
  private apiClient?: AgonAPIClient;
  private sessionManager?: SessionManager;
  private initializeKeypressEvents: () => void = () => {};
  private readonly promptHistory = new PromptHistory();

  public async run(): Promise<void> {
    const configManager = new ConfigManager();
    const sessionManager = new SessionManager();
    const config = await configManager.load();
    const apiClient = new AgonAPIClient(
      config.apiUrl,
      this.config.pjson.name ?? '@agon_agents/cli',
      this.config.pjson.version ?? '0.0.0'
    );
    this.apiClient = apiClient;
    this.sessionManager = sessionManager;
    this.initializeKeypressEvents = createKeypressInitializer(input);

    const controller = new ShellController({
      apiClient,
      sessionManager,
      configManager
    });

    const engine = new ShellEngine({
      controller,
      routePlainInput,
      selfUpdate: (options) => this.runSelfUpdate(options.check),
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
    const updateInfo = await checkForCliUpdate({
      packageName: this.config.pjson.name ?? '@agon_agents/cli',
      currentVersion: this.config.pjson.version ?? '0.0.0'
    });
    if (updateInfo) {
      this.log(chalk.yellow(`Update available: v${updateInfo.currentVersion} → v${updateInfo.latestVersion}`));
      this.log(chalk.cyan('Run now in this shell:'));
      this.log(chalk.cyan('  /update'));
      this.log(chalk.dim('Tip: Use /update --check to only verify availability.'));
      this.log(chalk.dim('If that fails, run:'));
      this.log(chalk.dim(`  ${updateInfo.installCommand}`));
    }
    this.log('');

    try {
      while (true) {
        const promptFrame = renderPromptBanner((line) => this.log(line));
        const rawInput = await this.promptForInput(promptFrame);

        if (rawInput === INTERRUPT_SENTINEL) {
          this.log(chalk.dim('Interrupted. Shell still active.'));
          this.log('');
          continue;
        }

        const trimmed = rawInput.trim();
        if (trimmed.length > 0 && !isExitInput(trimmed)) {
          this.promptHistory.push(trimmed);
        }

        if (isExitInput(trimmed)) {
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

        const abortController = new AbortController();
        const sigintHandler = (): void => {
          abortController.abort();
        };
        // process.once is intentional: the first Ctrl+C during an active
        // operation interrupts it and stays in the shell. A second Ctrl+C
        // (after the listener has been consumed and the operation is still
        // winding down) falls through to Node's default SIGINT handling and
        // exits the process — matching the "press twice to force-quit" UX.
        process.once('SIGINT', sigintHandler);

        try {
          const outcomePromise = engine.handleInput(trimmed);
          const outcome = await raceAbort(outcomePromise, abortController.signal);
          stopShimmer?.();
          if (spinner) {
            spinner.succeed('Done');
          }
          this.spinnerActive = false;
          this.flushPendingLines();
          await this.renderOutcome(outcome, abortController.signal);
        } catch (error) {
          stopShimmer?.();
          if (spinner) {
            if (isAbortError(error)) {
              spinner.stop();
            } else {
              spinner.fail('Failed');
            }
          }
          this.spinnerActive = false;
          this.flushPendingLines();
          if (isAbortError(error)) {
            this.log(chalk.dim('Interrupted. Shell still active.'));
          } else {
            const message = error instanceof Error ? error.message : String(error);
            this.log(chalk.red(`Error: ${message}`));
          }
        } finally {
          process.removeListener('SIGINT', sigintHandler);
        }

        this.log('');
      }
    } finally {
      this.restoreTerminalState();
    }
  }

  private async renderOutcome(outcome: ShellEngineOutcome, signal?: AbortSignal): Promise<void> {
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
          this.log('Next steps:');
          this.log('  • Continue in this shell: type your next message');
          this.log('  • Attach a document: /attach "<file-path>"');
          this.log('  • Explicit follow-up: /follow-up "<follow-up request>"');
          this.log('  • Start a new session: /new');
          this.log('  • Exit shell: /exit');
        } else {
          this.log(chalk.yellow('No response yet. Run /status or send your next input when ready.'));
        }
        return;
      case 'follow-up':
        if (outcome.response?.message) {
          const title = outcome.response.agentId === 'moderator' ? 'Moderator' : 'Assistant';
          const color = title === 'Moderator' ? 'cyan' : 'green';
          renderMessagePanel(title, outcome.response.message, color, (line) => this.log(line));
          this.log('Next steps:');
          this.log('  • Continue in this shell: type your next message');
          this.log('  • Attach a document: /attach "<file-path>"');
          this.log('  • Explicit follow-up: /follow-up "<follow-up request>"');
          this.log('  • Start a new session: /new');
          this.log('  • Exit shell: /exit');
        } else if (this.isMidDebatePhase(outcome.phase)) {
          await this.watchDebateProgress(outcome.sessionId, signal);
        } else {
          const phase = this.formatPhaseForDisplay(outcome.phase);
          this.log(chalk.yellow(`No new assistant/moderator response yet. Current phase: ${phase}.`));
          this.log(chalk.dim(`Session ${outcome.sessionId} is ${outcome.status}. You can wait, or run /status.`));
        }
        return;
      case 'status':
        this.log(`Session ${outcome.sessionId} | status=${outcome.status} | phase=${outcome.phase}`);
        return;
      case 'attachment':
        this.log(chalk.green(`✓ Attached ${outcome.fileName} to session ${outcome.sessionId}`));
        this.log(chalk.dim(`Type: ${outcome.contentType} | Size: ${formatBytes(outcome.sizeBytes)}`));
        if (outcome.hasExtractedText) {
          this.log(chalk.dim('Document text extracted and added to agent context.'));
        } else {
          this.log(chalk.dim('No text extraction available; file metadata/link still added to context.'));
        }
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

  private async runSelfUpdate(checkOnly: boolean): Promise<ShellSelfUpdateResult> {
    const packageName = this.config.pjson.name ?? '@agon_agents/cli';
    const currentVersion = this.config.pjson.version ?? '0.0.0';
    const updateInfo = await checkForCliUpdate({ packageName, currentVersion });
    const installCommand = `npm install -g ${packageName}@latest`;

    if (!updateInfo) {
      return {
        status: 'up-to-date',
        currentVersion
      };
    }

    if (checkOnly) {
      return {
        status: 'update-available',
        currentVersion: updateInfo.currentVersion,
        latestVersion: updateInfo.latestVersion,
        installCommand: updateInfo.installCommand
      };
    }

    try {
      await runNpmGlobalInstall(packageName);
      return {
        status: 'updated',
        currentVersion: updateInfo.currentVersion,
        latestVersion: updateInfo.latestVersion
      };
    } catch (error) {
      const failure = describeSelfUpdateFailure(error);
      return {
        status: 'failed',
        currentVersion: updateInfo.currentVersion,
        latestVersion: updateInfo.latestVersion,
        reason: failure.category,
        message: failure.message,
        guidance: getSelfUpdateGuidance(failure.category, installCommand),
        installCommand
      };
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

    this.initializeKeypressEvents();
    input.setRawMode(true);
    input.resume();
    this.inRawInputMode = true;

    output.write(`\u001b[${frame.cursorUpLines}A\r`);
    output.write(buildActivePrompt(frame));

    return await new Promise<string>((resolve) => {
      let value = '';
      let cursorLineIndex = getPromptCursorPosition(frame, value, 0).lineIndex;
      let cursorIndex = 0;

      this.promptHistory.reset();

      const redraw = (): void => {
        if (cursorLineIndex > 0) {
          output.write(`\u001b[${cursorLineIndex}A`);
        }
        output.write('\r');
        output.write(buildPromptInputLineWithCursor(frame, value, cursorIndex));
        cursorLineIndex = getPromptCursorPosition(frame, value, cursorIndex).lineIndex;
      };

      const finish = (result: string): void => {
        input.off('keypress', onKeypress);
        input.setRawMode(false);
        this.inRawInputMode = false;
        const cursorDownLines = Math.max(0, frame.cursorDownFromFirstLine - cursorLineIndex);
        if (cursorDownLines > 0) {
          output.write(`${frame.reset}\u001b[${cursorDownLines}B\r`);
        } else {
          output.write(`${frame.reset}\r`);
        }
        resolve(result);
      };

      const onKeypress = (str: string, key?: Key): void => {
        const keyName = key?.name;
        const isCtrl = Boolean(key?.ctrl);
        const isMeta = Boolean(key?.meta);
        const keySequence = key?.sequence;

        if (isCtrl && keyName === 'c') {
          finish(INTERRUPT_SENTINEL);
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
          if (keyName === 'backspace' && cursorIndex > 0) {
            value = `${value.slice(0, cursorIndex - 1)}${value.slice(cursorIndex)}`;
            cursorIndex -= 1;
            redraw();
          } else if (keyName === 'delete' && cursorIndex < value.length) {
            value = `${value.slice(0, cursorIndex)}${value.slice(cursorIndex + 1)}`;
            redraw();
          }
          return;
        }

        if (keyName === 'escape' || keyName === 'tab') {
          return;
        }

        if (isCtrl && keyName === 'a') {
          cursorIndex = 0;
          redraw();
          return;
        }

        if (isCtrl && keyName === 'e') {
          cursorIndex = value.length;
          redraw();
          return;
        }

        if (isCtrl && keyName === 'w') {
          const target = findPreviousWordBoundary(value, cursorIndex);
          if (target < cursorIndex) {
            value = `${value.slice(0, target)}${value.slice(cursorIndex)}`;
            cursorIndex = target;
            redraw();
          }
          return;
        }

        if (keyName === 'left') {
          if (isMeta) {
            cursorIndex = findPreviousWordBoundary(value, cursorIndex);
          } else if (cursorIndex > 0) {
            cursorIndex -= 1;
          }
          redraw();
          return;
        }

        if (keyName === 'right') {
          if (isMeta) {
            cursorIndex = findNextWordBoundary(value, cursorIndex);
          } else if (cursorIndex < value.length) {
            cursorIndex += 1;
          }
          redraw();
          return;
        }

        if (keyName === 'up') {
          if (this.promptHistory.isNavigating() || value === '') {
            const prev = this.promptHistory.navigateBack(value);
            if (prev !== undefined) {
              value = prev;
              cursorIndex = value.length;
              redraw();
            }
          } else {
            cursorIndex = Math.max(0, cursorIndex - frame.maxInputCharsPerLine);
            redraw();
          }
          return;
        }

        if (keyName === 'down') {
          if (this.promptHistory.isNavigating()) {
            const next = this.promptHistory.navigateForward();
            if (next !== undefined) {
              value = next;
              cursorIndex = value.length;
              redraw();
            }
          } else {
            cursorIndex = Math.min(value.length, cursorIndex + frame.maxInputCharsPerLine);
            redraw();
          }
          return;
        }

        if (isMeta && keyName === 'b') {
          cursorIndex = findPreviousWordBoundary(value, cursorIndex);
          redraw();
          return;
        }

        if (isMeta && keyName === 'f') {
          cursorIndex = findNextWordBoundary(value, cursorIndex);
          redraw();
          return;
        }

        if (str && !isCtrl && !isMeta && str !== '\r' && str !== '\n') {
          const clean = str.replace(/[\r\n]/g, '');
          if (clean.length === 0) {
            return;
          }
          const insertText = clean;
          value = `${value.slice(0, cursorIndex)}${insertText}${value.slice(cursorIndex)}`;
          cursorIndex += insertText.length;
          redraw();
        }
      };

      input.on('keypress', onKeypress);
    });
  }

  private restoreTerminalState(): void {
    input.removeAllListeners('keypress');

    if (this.inRawInputMode && input.isTTY && typeof input.setRawMode === 'function') {
      input.setRawMode(false);
      this.inRawInputMode = false;
    } else if (input.isTTY && typeof input.setRawMode === 'function') {
      input.setRawMode(false);
    }

    input.pause();
    output.write('\u001b[0m\u001b[?25h\r\n');
  }

  private formatPhaseForDisplay(phase: string): string {
    const compact = phase.replace(/[\s_-]/g, '').toLowerCase();
    const displayMap: Record<string, string> = {
      intake: 'Intake',
      clarification: 'Clarification',
      analysisround: 'Analysis Round',
      critique: 'Critique',
      synthesis: 'Synthesis',
      targetedloop: 'Targeted Loop',
      deliver: 'Deliver',
      deliverwithgaps: 'Deliver With Gaps',
      postdelivery: 'Post-Delivery'
    };
    return displayMap[compact] ?? phase;
  }

  private isMidDebatePhase(phase: string): boolean {
    const compact = phase.replace(/[\s_-]/g, '').toLowerCase();
    return compact === 'analysisround'
      || compact === 'critique'
      || compact === 'synthesis'
      || compact === 'targetedloop';
  }

  private async watchDebateProgress(sessionId: string, signal?: AbortSignal): Promise<void> {
    if (!this.apiClient || !this.sessionManager) {
      this.log(chalk.yellow('Debate is still running. Use /status to check progress.'));
      return;
    }

    const seenMessageKeys = new Set<string>();
    let lastPhase = '';
    let lastStatus = '';
    const watchStartedAt = Date.now();
    let lastProgressAt = watchStartedAt;
    let consecutiveFailures = 0;
    const maxWatchDurationMs = getWatchDurationMsFromEnv('AGON_WATCH_MAX_MINUTES', 25);
    const maxIdleDurationMs = getWatchDurationMsFromEnv('AGON_WATCH_MAX_IDLE_MINUTES', 8);
    const maxConsecutiveFailures = 5;
    let shimmerBase = 'Agents are analyzing your idea...';
    let shimmerTick = 0;
    const progressSpinner = ora({
      text: buildShimmerText(shimmerBase, shimmerTick),
      color: 'cyan'
    }).start();
    const shimmerInterval = setInterval(() => {
      shimmerTick += 1;
      progressSpinner.text = buildShimmerText(shimmerBase, shimmerTick);
    }, 90);
    if (typeof shimmerInterval.unref === 'function') {
      shimmerInterval.unref();
    }

    try {
      while (true) {
        if (signal?.aborted) {
          progressSpinner.stop();
          this.log(chalk.dim('Interrupted. Shell still active.'));
          return;
        }

        if (Date.now() - watchStartedAt > maxWatchDurationMs) {
          progressSpinner.stop();
          this.log(chalk.yellow('Watch timed out before completion.'));
          this.log(chalk.dim('Run /status and /refresh verdict to continue.'));
          return;
        }

        let session;
        let messages;
        try {
          [session, messages] = await Promise.all([
            this.apiClient.getSession(sessionId),
            this.apiClient.getMessages(sessionId)
          ]);
          consecutiveFailures = 0;
        } catch (error) {
          consecutiveFailures += 1;
          if (consecutiveFailures >= maxConsecutiveFailures) {
            progressSpinner.stop();
            const message = error instanceof Error ? error.message : String(error);
            this.log(chalk.red(`Stopped watching after repeated fetch failures: ${message}`));
            this.log(chalk.dim('Run /status and retry once connectivity is stable.'));
            return;
          }
          await new Promise(resolve => setTimeout(resolve, 1500 * consecutiveFailures));
          continue;
        }

        await this.sessionManager.saveSession(session);
        shimmerBase = `Agents are analyzing... (${this.formatPhaseForDisplay(session.phase)})`;

        const agentMessages = messages
          .filter(m => {
            const agent = m.agentId.toLowerCase();
            return agent !== 'moderator' && agent !== 'user';
          })
          .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());

        let pausedForOutput = false;
        const stopSpinnerForOutput = (): void => {
          if (!pausedForOutput) {
            progressSpinner.stop();
            pausedForOutput = true;
          }
        };

        for (const msg of agentMessages) {
          const key = `${msg.agentId}:${msg.round}:${msg.createdAt}:${msg.message.length}`;
          if (seenMessageKeys.has(key)) continue;
          seenMessageKeys.add(key);
          lastProgressAt = Date.now();

          stopSpinnerForOutput();
          this.log('━'.repeat(60));
          this.log(chalk.bold(`${msg.agentId} (Round ${msg.round})`));
          this.log('');
          this.log(renderMarkdown(msg.message));
          this.log('');
        }

        if (session.phase !== lastPhase) {
          stopSpinnerForOutput();
          this.log(chalk.dim(`⏱️  Current phase: ${this.formatPhaseForDisplay(session.phase)}`));
          this.log('');
          lastPhase = session.phase;
          lastProgressAt = Date.now();
        }

        const normalizedStatus = normalizeStatus(session.status);
        if (normalizedStatus !== lastStatus) {
          lastStatus = normalizedStatus;
          lastProgressAt = Date.now();
        }
        if (normalizedStatus === 'complete' || normalizedStatus === 'complete_with_gaps') {
          stopSpinnerForOutput();
          this.log(chalk.green('✓ Debate complete. Fetching final verdict...'));
          this.log('');
          const verdict = await this.apiClient.getArtifact(sessionId, 'verdict');
          await this.sessionManager.saveArtifact(sessionId, 'verdict', verdict.content);
          this.log('━'.repeat(60));
          this.log(chalk.bold('BEGIN FINAL OUTPUT'));
          this.log('━'.repeat(60));
          this.log(chalk.bold('Final Verdict'));
          this.log('');
          this.log(renderMarkdown(verdict.content));
          this.log('');
          this.log('━'.repeat(60));
          this.log(chalk.bold('END FINAL OUTPUT'));
          this.log('');
          this.log('Next steps:');
          this.log('  • Ask follow-up questions: /follow-up "<follow-up request>"');
          this.log('  • Attach a document: /attach "<file-path>"');
          this.log('  • Start a new session: /new');
          this.log('  • View again: /refresh verdict');
          this.log('  • Exit shell: /exit');
          return;
        }

        if (normalizedStatus !== 'active' && normalizedStatus !== 'paused') {
          stopSpinnerForOutput();
          this.log(chalk.yellow(`Debate stopped with status "${session.status}". Run /status for details.`));
          return;
        }

        if (Date.now() - lastProgressAt > maxIdleDurationMs) {
          stopSpinnerForOutput();
          this.log(chalk.yellow('No debate progress detected for a while. Stopping live watch.'));
          this.log(chalk.dim('Run /status and /refresh verdict to continue.'));
          return;
        }

        if (pausedForOutput) {
          progressSpinner.start();
        }

        await new Promise(resolve => setTimeout(resolve, 2500));
      }
    } finally {
      clearInterval(shimmerInterval);
      progressSpinner.stop();
    }
  }
}

function getWatchDurationMsFromEnv(envKey: string, fallbackMinutes: number): number {
  const raw = process.env[envKey];
  if (!raw) {
    return fallbackMinutes * 60_000;
  }

  const parsed = Number.parseInt(raw, 10);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return fallbackMinutes * 60_000;
  }

  return parsed * 60_000;
}

function isWordCharacter(char: string): boolean {
  return /[A-Za-z0-9_]/.test(char);
}

function findPreviousWordBoundary(value: string, cursorIndex: number): number {
  let index = Math.max(0, Math.min(cursorIndex, value.length));
  if (index === 0) {
    return 0;
  }

  while (index > 0 && !isWordCharacter(value[index - 1] ?? '')) {
    index -= 1;
  }
  while (index > 0 && isWordCharacter(value[index - 1] ?? '')) {
    index -= 1;
  }

  return index;
}

function findNextWordBoundary(value: string, cursorIndex: number): number {
  let index = Math.max(0, Math.min(cursorIndex, value.length));
  if (index >= value.length) {
    return value.length;
  }

  while (index < value.length && !isWordCharacter(value[index] ?? '')) {
    index += 1;
  }
  while (index < value.length && isWordCharacter(value[index] ?? '')) {
    index += 1;
  }

  return index;
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
    case 'update':
      return parsed.check ? 'Checking for CLI updates...' : 'Updating CLI...';
    case 'set':
      return 'Saving parameter...';
    case 'session':
      return 'Switching session...';
    case 'resume':
      return 'Resuming session...';
    case 'show-sessions':
      return 'Loading sessions...';
    case 'status':
      return 'Fetching status...';
    case 'show':
    case 'refresh':
      return 'Fetching artifact...';
    case 'attach':
      return 'Uploading attachment...';
    case 'follow-up':
      return 'Submitting follow-up...';
  }
}

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 1024) {
    return `${Math.max(0, Math.round(bytes))} B`;
  }

  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value.toFixed(1)} ${units[unitIndex]}`;
}

export function isExitInput(inputText: string): boolean {
  const trimmed = inputText.trim().toLowerCase();
  return trimmed === '/exit' || trimmed === '/quit' || trimmed === '/eot';
}
