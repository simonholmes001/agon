import { Command } from '@oclif/core';
import ora from 'ora';
import chalk from 'chalk';
import { type Key } from 'node:readline';
import { stdin as input, stdout as output } from 'node:process';
import path from 'node:path';
import { AgonAPIClient } from '../api/agon-client.js';
import { ConfigManager } from '../state/config-manager.js';
import { SessionManager } from '../state/session-manager.js';
import { AuthManager } from '../auth/auth-manager.js';
import { allowsAnonymousBypass, hasConfiguredAuthToken } from '../auth/auth-policy.js';
import { ShellController } from '../shell/controller.js';
import { createKeypressInitializer } from '../shell/keypress.js';
import {
  ShellEngine,
  type ShellEngineOutcome,
  type ShellSelfUpdateResult
} from '../shell/engine.js';
import { extractImplicitAttach, extractInlineAttach, parseShellInput } from '../shell/parser.js';
import { routePlainInput } from '../shell/router.js';
import {
  buildActivePrompt,
  buildInterruptHint,
  buildShimmerText,
  buildPromptInputLineWithCursor,
  formatElapsedTimer,
  getWrappedLineCount,
  getPromptCursorPosition,
  type PromptFrameContext,
  renderMessagePanel,
  renderPromptBanner,
  renderShellHeader,
  renderStatusLine,
  styleAttachmentToken
} from '../shell/renderer.js';
import { PromptHistory } from '../shell/history.js';
import { renderMarkdown } from '../utils/markdown.js';
import { normalizeStatus } from '../utils/session-flow.js';
import { checkForCliUpdate } from '../utils/update-check.js';
import { AgonError, ErrorCode } from '../utils/error-handler.js';
import {
  describeSelfUpdateFailure,
  getSelfUpdateGuidance,
  runNpmGlobalInstall
} from '../utils/self-update.js';
import { buildRuntimeExecutionProfile } from '../runtime/user-runtime-profile.js';
import { AGENT_MODEL_IDS } from '../state/agent-model-config.js';

/**
 * Sentinel value returned from promptForInput when the user presses Ctrl+C
 * while the input zone is non-empty. The main loop treats this as
 * "interrupt current operation and stay in shell" rather than exiting.
 */
export const INTERRUPT_SENTINEL = '\x03';

/**
 * Sentinel value returned from promptForInput when the user presses Ctrl+C
 * while the input zone is empty. The main loop treats this as a request to
 * exit the shell session entirely.
 */
export const CTRL_C_EXIT_SENTINEL = '\x1C';

/**
 * Returns the appropriate sentinel for a Ctrl+C keypress based on whether
 * the current input zone value is empty.
 *
 * - Empty input → CTRL_C_EXIT_SENTINEL (exit the shell)
 * - Non-empty input → INTERRUPT_SENTINEL (interrupt only, stay in shell)
 */
export function selectCtrlCSentinel(inputValue: string): typeof CTRL_C_EXIT_SENTINEL | typeof INTERRUPT_SENTINEL {
  return inputValue.length === 0 ? CTRL_C_EXIT_SENTINEL : INTERRUPT_SENTINEL;
}

/**
 * Normalize inserted keypress chunks so pasted multiline content keeps line
 * breaks consistently across CRLF/LF terminals.
 */
export function normalizePromptInsertText(chunk: string): string {
  return chunk.replace(/\r\n?/g, '\n');
}


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
  private readonly livePreviewNextBySession = new Map<string, { image: number; file: number }>();
  private initializeKeypressEvents: () => void = () => {};
  private readonly promptHistory = new PromptHistory();

  public async run(): Promise<void> {
    const configManager = new ConfigManager();
    const sessionManager = new SessionManager();
    const authManager = new AuthManager();
    const config = await configManager.load();

    // Resolve auth token: env var > stored credentials
    const storedToken = await authManager.getToken();
    const runtimeProfile = await buildRuntimeExecutionProfile(storedToken);

    const apiClient = new AgonAPIClient(
      config.apiUrl,
      this.config.pjson.name ?? '@agon_agents/cli',
      this.config.pjson.version ?? '0.0.0',
      storedToken ?? undefined,
      runtimeProfile.profile
    );
    this.apiClient = apiClient;
    this.sessionManager = sessionManager;
    this.initializeKeypressEvents = createKeypressInitializer(input);

    if (runtimeProfile.missingProviders.length > 0) {
      this.log(
        chalk.yellow(
          `Missing API keys for providers: ${runtimeProfile.missingProviders.join(', ')}.`,
        ),
      );
      this.log(
        chalk.dim(
          'Run in your terminal (outside the Agon shell): `agon command onboard` (recommended) ' +
          'or `agon keys set <provider>` before starting a debate.'
        )
      );
      this.log('');
    }

    // Pre-flight: auth check
    // Only block when the backend explicitly requires authentication AND the
    // user has no token configured. We do not block on network errors so
    // that self-hosted setups still work while the backend is starting up.
    const authStatus = await apiClient.getAuthStatus();
    const hasToken = hasConfiguredAuthToken(storedToken);
    const allowAnonymous = allowsAnonymousBypass();

    if (!hasToken && !allowAnonymous) {
      this.log('');
      this.log(chalk.red('✗ Authentication required'));
      this.log('');
      if (authStatus?.required) {
        this.log(`The Agon backend at ${chalk.cyan(config.apiUrl)} requires a bearer token.`);
      } else {
        this.log(`No bearer token is configured for backend ${chalk.cyan(config.apiUrl)}.`);
      }
      this.log('');
      this.log('First-time setup:');
      this.log(`  ${chalk.cyan('agon login')}              Save your bearer token`);
      this.log(`  ${chalk.cyan('agon login --status')}     Check current auth status`);
      this.log('');
      this.log(chalk.dim('Tip: `agon login` auto-discovers tenant/scope from backend auth metadata when available.'));
      this.log('');
      this.exit(1);
    }

    if (authStatus?.required && hasToken) {
      try {
        await apiClient.listSessions();
      } catch (error) {
        if (isUnauthenticatedError(error)) {
          this.printAuthRejectedMessage(config.apiUrl);
          this.exit(1);
        }
      }
    }

    if (!hasToken && allowAnonymous) {
      this.log('');
      this.log(chalk.yellow('ℹ Running without bearer token (anonymous bypass enabled).'));
      this.log(chalk.dim(`  Backend: ${config.apiUrl}`));
      this.log(chalk.dim('  Set AGON_ALLOW_ANONYMOUS=false (or unset it) to enforce login.'));
      this.log('');
      this.log('Recommended first-time setup:');
      this.log(`  ${chalk.cyan('agon login')}              Save your bearer token`);
      this.log(`  ${chalk.cyan('agon login --status')}     Check current auth status`);
      this.log('');
    }

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
    const agentSetupLines = AGENT_MODEL_IDS.map((agentId) => {
      const selection = runtimeProfile.profile.agentModels[agentId];
      return `${agentId}: ${selection.provider}/${selection.model}`;
    });
    renderShellHeader(
      {
        version: this.config.pjson.version ?? 'dev',
        modelLabel: 'OpenAI GPT (backend configured)',
        directory: process.cwd(),
        config: snapshot.config,
        session: snapshot.session,
        agentSetup: agentSetupLines
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
        this.log('');
        const promptFrame = renderPromptBanner((line) => this.log(line));
        const activeSession = await controller.getActiveSession();
        const rawInput = await this.promptForInput(promptFrame, activeSession?.id ?? null);

        if (rawInput === CTRL_C_EXIT_SENTINEL) {
          this.log(chalk.dim('Exiting shell.'));
          return;
        }

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
          try {
            const activeSession = await controller.getActiveSession();
            if (activeSession) {
              this.log(chalk.dim(buildExitResumeHint(activeSession.id)));
            }
          } catch {
            // Best-effort: ignore errors in the exit path
          }
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
          } else if (isUnauthenticatedError(error)) {
            this.printAuthRejectedMessage(config.apiUrl);
            return;
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
          this.log('  • Add a file: paste/drag a local file path into the input box');
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
          this.log('  • Add a file: paste/drag a local file path into the input box');
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
      case 'attachment': {
        this.syncLivePreviewCounter(outcome.sessionId, outcome.referenceLabel);
        const attachedLine = chalk.green('✓ Attached ')
          + styleAttachmentToken(outcome.referenceLabel)
          + chalk.green(' ')
          + chalk.dim(`(${outcome.fileName})`)
          + chalk.green(` to session ${outcome.sessionId}`);
        this.log(attachedLine);
        this.log(chalk.dim(`Type: ${outcome.contentType} | Size: ${formatBytes(outcome.sizeBytes)}`));
        if (outcome.hasExtractedText) {
          this.log(chalk.dim('Attachment content extracted and added to agent context.'));
        } else {
          if (outcome.contentType.toLowerCase().startsWith('image/')) {
            this.log(
              chalk.yellow(
                'Image uploaded, but backend vision extraction returned no content. '
                + 'Check OPENAI_KEY and ATTACHMENTPROCESSING__OPENAIVISION__ENABLED on backend.'
              )
            );
          } else {
            this.log(chalk.dim('No text extraction available; file metadata/link still added to context.'));
          }
        }
        return;
      }
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
    const startedAt = Date.now();
    const hint = buildInterruptHint();
    spinner.text = `${buildShimmerText(baseText, tick)} ${formatElapsedTimer(startedAt)}  ${hint}`;

    const interval = setInterval(() => {
      tick += 1;
      spinner.text = `${buildShimmerText(baseText, tick)} ${formatElapsedTimer(startedAt)}  ${hint}`;
    }, 90);

    if (typeof interval.unref === 'function') {
      interval.unref();
    }

    return () => clearInterval(interval);
  }

  private async promptForInput(
    frame: PromptFrameContext,
    activeSessionId: string | null
  ): Promise<string> {
    if (!input.isTTY || typeof input.setRawMode !== 'function') {
      this.log(chalk.red('Interactive shell input requires a TTY.'));
      return '/quit';
    }

    this.initializeKeypressEvents();
    input.setRawMode(true);
    input.resume();
    this.inRawInputMode = true;

    let currentFrame = frame;
    const minInputLineCount = frame.inputLineCount;
    const maxInputLineCount = Math.max(
      minInputLineCount,
      Math.min((output.rows ?? 24) - 8, 18)
    );

    output.write(`\u001b[${currentFrame.cursorUpLines}A\r`);
    output.write(buildActivePrompt(currentFrame));

    return await new Promise<string>((resolve) => {
      let value = '';
      let cursorLineIndex = getPromptCursorPosition(currentFrame, value, 0).lineIndex;
      let cursorIndex = 0;

      this.promptHistory.reset();

      const resizePromptFrame = (nextInputLineCount: number): void => {
        if (nextInputLineCount === currentFrame.inputLineCount) {
          return;
        }
        // Move to zone row 0 (not above it) so new rows are appended at the
        // bottom.  If the zone is at the terminal viewport bottom the terminal
        // scrolls naturally, pushing old content up — matching Codex behaviour.
        const moveUpLines = cursorLineIndex;
        if (moveUpLines > 0) {
          output.write(`\u001b[${moveUpLines}A\r`);
        } else {
          output.write('\r');
        }
        output.write('\u001b[0J');
        currentFrame = renderPromptBanner(
          (line) => output.write(`${line}\n`),
          { inputLineCount: nextInputLineCount }
        );
        // Position cursor at newCursorLineIndex within the new frame.
        // redraw() will then move up newCursorLineIndex lines to reach row 0.
        const newCursorLineIndex = getPromptCursorPosition(currentFrame, value, cursorIndex).lineIndex;
        const upToNewCursorLine = currentFrame.cursorUpLines - newCursorLineIndex;
        if (upToNewCursorLine > 0) {
          output.write(`\u001b[${upToNewCursorLine}A\r`);
        } else {
          output.write('\r');
        }
        cursorLineIndex = newCursorLineIndex;
      };

      const redraw = (): void => {
        const preview = this.buildLiveAttachmentPreview(value, cursorIndex, activeSessionId);
        const requiredLines = getWrappedLineCount(preview.displayValue, currentFrame.maxInputCharsPerLine);
        const desiredInputLineCount = Math.min(
          maxInputLineCount,
          Math.max(minInputLineCount, currentFrame.promptLineOffset + requiredLines + 1)
        );
        if (desiredInputLineCount !== currentFrame.inputLineCount) {
          resizePromptFrame(desiredInputLineCount);
        }
        const rendered = buildPromptInputLineWithCursor(
          currentFrame,
          preview.displayValue,
          preview.displayCursorIndex
        );
        const cursorAnchor = rendered.lastIndexOf('\r');
        const beforeCursor = cursorAnchor >= 0 ? rendered.slice(0, cursorAnchor) : rendered;
        const afterCursor = cursorAnchor >= 0 ? rendered.slice(cursorAnchor) : '';
        const styledBefore = preview.highlightSegment
          ? stylePromptAttachmentSegment(beforeCursor, preview.highlightSegment)
          : beforeCursor;
        const styledAfter = preview.highlightSegment
          ? stylePromptAttachmentSegment(afterCursor, preview.highlightSegment)
          : afterCursor;
        const styled = `${styledBefore}${styledAfter}`;
        if (cursorLineIndex > 0) {
          output.write(`\u001b[${cursorLineIndex}A`);
        }
        output.write('\r');
        output.write(styled);
        cursorLineIndex = getPromptCursorPosition(currentFrame, value, cursorIndex).lineIndex;
      };

      const finish = (result: string): void => {
        input.off('keypress', onKeypress);
        input.setRawMode(false);
        this.inRawInputMode = false;
        const cursorDownLines = Math.max(0, currentFrame.cursorDownFromFirstLine - cursorLineIndex);
        if (cursorDownLines > 0) {
          output.write(`${currentFrame.reset}\u001b[${cursorDownLines}B\r`);
        } else {
          output.write(`${currentFrame.reset}\r`);
        }
        resolve(result);
      };

      const onKeypress = (str: string, key?: Key): void => {
        const keyName = key?.name;
        const isCtrl = Boolean(key?.ctrl);
        const isMeta = Boolean(key?.meta);
        const keySequence = key?.sequence;

        if (isCtrl && keyName === 'c') {
          finish(selectCtrlCSentinel(value));
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
          const clean = normalizePromptInsertText(str);
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

  private printAuthRejectedMessage(apiUrl: string): void {
    this.log('');
    this.log(chalk.red('✗ Authentication required'));
    this.log('');
    this.log(`The Agon backend at ${chalk.cyan(apiUrl)} rejected your token.`);
    this.log('');
    this.log('Run in terminal:');
    this.log(`  ${chalk.cyan('agon login')}              Refresh or save bearer token`);
    this.log(`  ${chalk.cyan('agon login --status')}     Check current auth status`);
    this.log('');
    this.log(chalk.dim('After login, restart `agon` and retry.'));
    this.log('');
  }

  private buildLiveAttachmentPreview(
    inputValue: string,
    cursorIndex: number,
    activeSessionId: string | null
  ): {
    displayValue: string;
    displayCursorIndex: number;
    highlightSegment: string | null;
  } {
    const baseline = {
      displayValue: inputValue,
      displayCursorIndex: cursorIndex,
      highlightSegment: null
    };

    const inlineAttach = extractInlineAttach(inputValue);
    if (inlineAttach?.type === 'attach' || inlineAttach?.type === 'error') {
      return baseline;
    }

    const implicitAttach = extractImplicitAttach(inputValue);
    if (implicitAttach?.type !== 'attach') {
      return baseline;
    }

    const rawPathRange = findRawAttachmentPathRange(inputValue, implicitAttach.path);
    if (!rawPathRange) {
      return baseline;
    }

    const isImage = isLikelyImagePath(implicitAttach.path);
    const nextToken = this.peekLivePreviewToken(activeSessionId, isImage ? 'image' : 'file');
    const token = isImage ? `[Image #${nextToken}]` : `[File #${nextToken}]`;
    const fileName = path.basename(implicitAttach.path);
    const replacement = `${token} ${fileName}`;
    const displayValue = `${inputValue.slice(0, rawPathRange.start)}${replacement}${inputValue.slice(rawPathRange.end)}`;

    const replacedLength = rawPathRange.end - rawPathRange.start;
    const delta = replacement.length - replacedLength;
    let displayCursorIndex = cursorIndex;
    if (cursorIndex > rawPathRange.end) {
      displayCursorIndex = cursorIndex + delta;
    } else if (cursorIndex > rawPathRange.start) {
      displayCursorIndex = rawPathRange.start + replacement.length;
    }

    return {
      displayValue,
      displayCursorIndex,
      highlightSegment: replacement
    };
  }

  private peekLivePreviewToken(
    sessionId: string | null,
    kind: 'image' | 'file'
  ): number {
    const key = sessionId ?? '__pending__';
    const counters = this.livePreviewNextBySession.get(key) ?? { image: 1, file: 1 };
    return kind === 'image' ? counters.image : counters.file;
  }

  private syncLivePreviewCounter(sessionId: string, referenceLabel: string): void {
    const imageMatch = /^\[Image\s+#(\d+)\]$/i.exec(referenceLabel);
    const fileMatch = /^\[File\s+#(\d+)\]$/i.exec(referenceLabel);

    if (!imageMatch && !fileMatch) {
      return;
    }

    const counters = this.livePreviewNextBySession.get(sessionId) ?? { image: 1, file: 1 };
    if (imageMatch) {
      const seen = Number.parseInt(imageMatch[1] ?? '0', 10);
      if (Number.isFinite(seen)) {
        counters.image = Math.max(counters.image, seen + 1);
      }
    }

    if (fileMatch) {
      const seen = Number.parseInt(fileMatch[1] ?? '0', 10);
      if (Number.isFinite(seen)) {
        counters.file = Math.max(counters.file, seen + 1);
      }
    }

    this.livePreviewNextBySession.set(sessionId, counters);
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
    let thinkingStartedAt = Date.now();
    const hint = buildInterruptHint();
    const progressSpinner = ora({
      text: `${buildShimmerText(shimmerBase, shimmerTick)} ${formatElapsedTimer(thinkingStartedAt)}  ${hint}`,
      color: 'cyan'
    }).start();
    const shimmerInterval = setInterval(() => {
      shimmerTick += 1;
      progressSpinner.text = `${buildShimmerText(shimmerBase, shimmerTick)} ${formatElapsedTimer(thinkingStartedAt)}  ${hint}`;
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
          thinkingStartedAt = Date.now();
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
          this.log('  • Add a file: paste/drag a local file path into the input box');
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
          thinkingStartedAt = Date.now();
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

function isLikelyImagePath(filePath: string): boolean {
  const extension = path.extname(filePath).toLowerCase();
  return extension === '.png'
    || extension === '.jpg'
    || extension === '.jpeg'
    || extension === '.gif'
    || extension === '.webp'
    || extension === '.bmp'
    || extension === '.tif'
    || extension === '.tiff'
    || extension === '.heic'
    || extension === '.heif';
}

function stylePromptAttachmentSegment(
  rendered: string,
  segment: string
): string {
  const escaped = escapeRegExp(segment);
  const segmentPattern = new RegExp(escaped, 'g');
  const accentStart = '\u001b[1m\u001b[38;2;132;255;142m';
  const accentEnd = '\u001b[22m\u001b[97m';
  return rendered.replace(segmentPattern, (match) => `${accentStart}${match}${accentEnd}`);
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function findRawAttachmentPathRange(
  input: string,
  normalizedPath: string
): { start: number; end: number } | null {
  const escapedSpaces = normalizedPath.replace(/ /g, '\\ ');
  const candidates = [
    `"${normalizedPath}"`,
    `'${normalizedPath}'`,
    `"${escapedSpaces}"`,
    `'${escapedSpaces}'`,
    escapedSpaces,
    normalizedPath
  ];

  for (const candidate of candidates) {
    const start = input.lastIndexOf(candidate);
    if (start >= 0) {
      return { start, end: start + candidate.length };
    }
  }

  return null;
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
    case 'unset':
      return 'Clearing parameter...';
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

function isUnauthenticatedError(error: unknown): boolean {
  return error instanceof AgonError && error.code === ErrorCode.UNAUTHENTICATED;
}

export function isExitInput(inputText: string): boolean {
  const trimmed = inputText.trim().toLowerCase();
  return trimmed === '/exit' || trimmed === '/quit' || trimmed === '/eot';
}

export function buildExitResumeHint(sessionId: string): string {
  return `To continue this session, run: agon resume ${sessionId}`;
}
