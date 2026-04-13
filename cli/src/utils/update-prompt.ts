import { emitKeypressEvents } from 'node:readline';
import chalk from 'chalk';
import { formatTerminalLink } from './terminal-links.js';
import type { CliUpdateInfo } from './update-check.js';

export type UpdatePromptChoice = 'update' | 'skip' | 'skip-version';

export const RELEASES_URL = 'https://github.com/simonholmes001/agon/releases/latest';

interface MenuItem {
  label: (installCommand: string) => string;
  choice: UpdatePromptChoice;
}

const MENU: MenuItem[] = [
  { label: (cmd) => `Update now (runs \`${cmd}\`)`, choice: 'update' },
  { label: () => 'Skip', choice: 'skip' },
  { label: () => 'Skip until next version', choice: 'skip-version' }
];

/** Number of output lines the menu occupies (items + blank + hint). */
const MENU_LINE_COUNT = MENU.length + 2;

/**
 * Display a Codex-style update notification with an interactive arrow-key /
 * number-key menu.  Falls back to a plain text notice if stdin is not a TTY.
 */
export async function showUpdatePrompt(updateInfo: CliUpdateInfo): Promise<UpdatePromptChoice> {
  const out = process.stdout;

  const releaseNotesLink = formatTerminalLink(RELEASES_URL, RELEASES_URL);

  out.write('\n');
  out.write(
    `${chalk.yellow('✨')} ${chalk.bold('Update available!')} ` +
    `${chalk.dim(updateInfo.currentVersion)} ${chalk.dim('→')} ${chalk.bold(updateInfo.latestVersion)}\n`
  );
  out.write(`${chalk.dim('Release notes:')} ${chalk.cyan(releaseNotesLink)}\n`);
  out.write('\n');

  // Non-interactive fallback (piped / non-TTY stdout or stdin).
  if (!process.stdin.isTTY || !process.stdout.isTTY) {
    renderMenuStatic(out, updateInfo.installCommand, 0);
    return 'skip';
  }

  return runInteractiveMenu(updateInfo.installCommand);
}

function renderMenuStatic(
  out: NodeJS.WriteStream,
  installCommand: string,
  selectedIndex: number
): void {
  for (let i = 0; i < MENU.length; i++) {
    const label = MENU[i].label(installCommand);
    if (i === selectedIndex) {
      out.write(`${chalk.green('> ')}${chalk.green(`${i + 1}. ${label}`)}\n`);
    } else {
      out.write(`  ${i + 1}. ${label}\n`);
    }
  }
  out.write('\n');
  out.write(chalk.dim('Press enter to continue\n'));
}

function clearMenu(out: NodeJS.WriteStream): void {
  out.write(`\u001b[${MENU_LINE_COUNT}A`); // move up
  out.write('\u001b[0J');                   // clear to end of screen
}

function runInteractiveMenu(installCommand: string): Promise<UpdatePromptChoice> {
  const inp = process.stdin;
  const out = process.stdout;

  return new Promise<UpdatePromptChoice>((resolve) => {
    let selectedIndex = 0;

    const render = (): void => renderMenuStatic(out, installCommand, selectedIndex);

    const finish = (choice: UpdatePromptChoice): void => {
      inp.setRawMode(false);
      inp.removeListener('keypress', onKeypress);
      inp.pause();
      resolve(choice);
    };

    const onKeypress = (_str: string | undefined, key?: { name?: string; sequence?: string; ctrl?: boolean }): void => {
      if (!key) {
        return;
      }

      // Ctrl+C — bail out as "skip"
      if (key.ctrl && key.name === 'c') {
        finish('skip');
        return;
      }

      if (key.name === 'up') {
        if (selectedIndex > 0) {
          clearMenu(out);
          selectedIndex--;
          render();
        }
        return;
      }

      if (key.name === 'down') {
        if (selectedIndex < MENU.length - 1) {
          clearMenu(out);
          selectedIndex++;
          render();
        }
        return;
      }

      // Number keys 1–N for direct selection + immediate confirm
      const digit = key.sequence ? Number(key.sequence) : NaN;
      if (!isNaN(digit) && digit >= 1 && digit <= MENU.length) {
        clearMenu(out);
        selectedIndex = digit - 1;
        render();
        finish(MENU[selectedIndex].choice);
        return;
      }

      // Enter confirms current selection
      if (key.name === 'return' || key.name === 'enter') {
        finish(MENU[selectedIndex].choice);
        return;
      }

      // Escape / q — treat as skip
      if (key.name === 'escape' || key.name === 'q') {
        finish('skip');
        return;
      }
    };

    render();

    emitKeypressEvents(inp);
    inp.setRawMode(true);
    inp.resume();
    inp.on('keypress', onKeypress);
  });
}
