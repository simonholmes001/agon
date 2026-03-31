import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function shouldPrintVersion(args) {
  return args.includes('--version') || args.includes('-v');
}

export function shouldSelfUpdate(args) {
  return args.includes('--self-update') || args[0] === 'self-update';
}

export function shouldPrintTopLevelHelp(args) {
  return args.length === 1 && (args[0] === '--help' || args[0] === '-h');
}

export const TOP_LEVEL_COMMAND_ROWS = [
  { command: 'answer <message>', description: 'Submit a clarification/follow-up message' },
  { command: 'command', description: 'Show current per-user model/key config' },
  { command: 'command show', description: 'Show current per-user model/key config' },
  { command: 'command status', description: 'Alias of command show' },
  { command: 'command onboard', description: 'Run interactive onboarding (keys + models)' },
  { command: 'command set-model <agent> <provider> <model>', description: 'Update agent model routing' },
  { command: 'command set-key <provider> [--key <value>]', description: 'Store provider API key' },
  { command: 'command rotate-key <provider> [--key <value>]', description: 'Rotate provider API key' },
  { command: 'command delete-key <provider> [--yes]', description: 'Delete provider API key' },
  { command: 'command recover-key <provider> [--yes] [--out <path>]', description: 'Reveal provider API key' },
  { command: 'config', description: 'Display current configuration' },
  { command: 'config set <key> <value>', description: 'Set a configuration override' },
  { command: 'config unset <key>', description: 'Remove a configuration override' },
  { command: 'follow-up <message>', description: 'Alias of answer <message>' },
  { command: 'keys', description: 'List stored provider API keys' },
  { command: 'keys set <provider> [--key <value>]', description: 'Store provider API key (shortcut)' },
  { command: 'keys rotate <provider> [--key <value>]', description: 'Rotate provider API key (shortcut)' },
  { command: 'keys delete <provider>', description: 'Delete provider API key (shortcut)' },
  { command: 'login', description: 'Set up authentication for the backend' },
  { command: 'login --status', description: 'Show current authentication status' },
  { command: 'login --clear', description: 'Clear stored authentication token' },
  { command: 'login --token <token>', description: 'Save bearer token non-interactively' },
  {
    command: 'login --device-code [--scope <scope>] [--tenant <tenant>] [--authority <url>] [--client-id <id>]',
    description: 'Sign in using Entra device-code flow'
  },
  {
    command: 'login --azure-cli [--scope <scope>] [--tenant <tenant>]',
    description: 'Acquire token from Azure CLI and save it'
  },
  { command: 'onboard', description: 'Run interactive onboarding (alias)' },
  { command: 'resume <session-id>', description: 'Resume a session and set it as current' },
  { command: 'self-update [--check]', description: 'Update CLI to latest version' },
  { command: 'sessions [--all]', description: 'List cached sessions' },
  { command: 'shell', description: 'Open interactive codex-style shell (default)' },
  { command: 'show <artifact>', description: 'Display an artifact from current session' },
  { command: 'start <idea>', description: 'Start a new strategy debate session' },
  { command: 'status [session-id] [--no-refresh]', description: 'Show current session status' }
];

function resolveCommandDirectories() {
  const binDir = path.dirname(fileURLToPath(import.meta.url));
  return [
    path.resolve(binDir, '../src/commands'),
    path.resolve(binDir, '../dist/commands')
  ];
}

function hasCuratedPrefix(prefix) {
  return TOP_LEVEL_COMMAND_ROWS.some(
    (row) => row.command === prefix || row.command.startsWith(`${prefix} `)
  );
}

function extractDescription(source) {
  const match = source.match(
    /static(?:\s+override)?(?:\s+readonly)?\s+description\s*=\s*(['"`])([\s\S]*?)\1\s*;/m
  );
  if (!match) {
    return null;
  }

  return match[2].replace(/\s+/g, ' ').trim();
}

function extractAliases(source) {
  const match = source.match(
    /static(?:\s+override)?(?:\s+readonly)?\s+aliases\s*=\s*\[([\s\S]*?)\]\s*;/m
  );
  if (!match) {
    return [];
  }

  return [...match[1].matchAll(/['"`]([^'"`]+)['"`]/g)].map((aliasMatch) => aliasMatch[1]);
}

function extractActionSwitchCases(source) {
  const switchIndex = source.indexOf('switch (args.action)');
  if (switchIndex === -1) {
    return [];
  }

  const openBraceIndex = source.indexOf('{', switchIndex);
  if (openBraceIndex === -1) {
    return [];
  }

  let depth = 0;
  let closeBraceIndex = -1;
  for (let i = openBraceIndex; i < source.length; i += 1) {
    const char = source[i];
    if (char === '{') {
      depth += 1;
      continue;
    }

    if (char === '}') {
      depth -= 1;
      if (depth === 0) {
        closeBraceIndex = i;
        break;
      }
    }
  }

  if (closeBraceIndex === -1) {
    return [];
  }

  const switchBody = source.slice(openBraceIndex + 1, closeBraceIndex);
  return [...switchBody.matchAll(/case\s+['"`]([^'"`]+)['"`]\s*:/g)].map((match) => match[1]);
}

function extractConfigIfActions(source) {
  return [...source.matchAll(/args\.action\s*===\s*['"`]([^'"`]+)['"`]/g)].map((match) => match[1]);
}

function dedupeRows(rows) {
  const unique = new Map();
  for (const row of rows) {
    if (!unique.has(row.command)) {
      unique.set(row.command, row);
    }
  }
  return [...unique.values()];
}

export function discoverTopLevelCommandRows() {
  const discoveredRows = [];
  const seenCommandIds = new Set();

  for (const directory of resolveCommandDirectories()) {
    if (!existsSync(directory)) {
      continue;
    }

    const files = readdirSync(directory)
      .filter((filename) => filename.endsWith('.ts') || filename.endsWith('.js'))
      .sort();

    for (const filename of files) {
      const commandId = filename.replace(/\.(ts|js)$/, '');
      if (seenCommandIds.has(commandId)) {
        continue;
      }

      seenCommandIds.add(commandId);

      const source = readFileSync(path.join(directory, filename), 'utf8');
      const description = extractDescription(source) ?? 'Show command help for details';
      if (!hasCuratedPrefix(commandId)) {
        discoveredRows.push({ command: commandId, description });
      }

      const aliases = extractAliases(source);
      for (const alias of aliases) {
        if (!hasCuratedPrefix(alias)) {
          discoveredRows.push({ command: alias, description: `Alias of ${commandId}` });
        }
      }

      for (const action of extractActionSwitchCases(source)) {
        const prefix = `${commandId} ${action}`;
        if (!hasCuratedPrefix(prefix)) {
          discoveredRows.push({ command: prefix, description: `${commandId} action` });
        }
      }

      if (commandId === 'config') {
        for (const action of extractConfigIfActions(source)) {
          const prefix = `${commandId} ${action}`;
          if (!hasCuratedPrefix(prefix)) {
            discoveredRows.push({ command: prefix, description: `${commandId} action` });
          }
        }
      }
    }
  }

  return dedupeRows(discoveredRows);
}

export function buildTopLevelHelp(binName = 'agon', discoveredRows = discoverTopLevelCommandRows()) {
  const mergedRows = dedupeRows([
    ...TOP_LEVEL_COMMAND_ROWS,
    ...discoveredRows
  ]);
  const sortedRows = [...mergedRows].sort((a, b) => a.command.localeCompare(b.command));
  const commandLabels = sortedRows.map(({ command }) => `  ${binName} ${command}`);
  const commandColumnWidth = Math.max(...commandLabels.map((label) => label.length)) + 2;
  const commandLines = sortedRows.map(({ description }, index) =>
    `${commandLabels[index].padEnd(commandColumnWidth)}${description}`
  );

  return [
    'Agon CLI',
    '',
    'USAGE',
    `  $ ${binName}`,
    `  $ ${binName} <command> [flags]`,
    '',
    'GLOBAL FLAGS',
    '  -h, --help         Show top-level help',
    '  -v, --version      Show installed CLI version',
    '  --self-update      Update CLI to latest version',
    '',
    'COMMANDS',
    ...commandLines,
    '',
    'NOTES',
    `  Run ${binName} <command> --help for command-specific flags.`,
    `  Run ${binName} /help inside the interactive shell for slash commands.`
  ].join('\n');
}
