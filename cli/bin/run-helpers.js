export function shouldPrintVersion(args) {
  return args.includes('--version') || args.includes('-v');
}

export function shouldSelfUpdate(args) {
  return args.includes('--self-update') || args[0] === 'self-update';
}

export function shouldPrintTopLevelHelp(args) {
  return args.length === 1 && (args[0] === '--help' || args[0] === '-h');
}

export function buildTopLevelHelp(binName = 'agon') {
  const commandRows = [
    { command: 'answer <message>', description: 'Submit a clarification/follow-up message' },
    { command: 'command onboard', description: 'Run interactive onboarding (keys + models)' },
    { command: 'command show', description: 'Show current per-user model/key config' },
    { command: 'command set-model <agent> <provider> <model>', description: 'Update agent model routing' },
    { command: 'command set-key <provider> --key <value>', description: 'Store provider API key' },
    { command: 'command rotate-key <provider> --key <value>', description: 'Rotate provider API key' },
    { command: 'command delete-key <provider> [--yes]', description: 'Delete provider API key' },
    { command: 'command recover-key <provider> --yes', description: 'Reveal provider API key' },
    { command: 'config [key] [value]', description: 'Display or modify configuration' },
    { command: 'keys <subcommand>', description: 'Manage provider API keys' },
    { command: 'keys set <provider> --key <value>', description: 'Store provider API key (shortcut)' },
    { command: 'keys rotate <provider> --key <value>', description: 'Rotate provider API key (shortcut)' },
    { command: 'keys delete <provider> [--yes]', description: 'Delete provider API key (shortcut)' },
    { command: 'keys recover <provider> --yes', description: 'Reveal provider API key (shortcut)' },
    { command: 'login', description: 'Set up authentication for the backend' },
    { command: 'onboard', description: 'Run interactive onboarding (alias)' },
    { command: 'resume [session-id]', description: 'Resume a session and set it as current' },
    { command: 'self-update [--check]', description: 'Update CLI to latest version' },
    { command: 'sessions', description: 'List cached sessions' },
    { command: 'shell', description: 'Open interactive codex-style shell (default)' },
    { command: 'show <artifact>', description: 'Display an artifact from current session' },
    { command: 'start <idea>', description: 'Start a new strategy debate session' },
    { command: 'status [session-id]', description: 'Show current session status' }
  ];
  const sortedRows = commandRows.sort((a, b) => a.command.localeCompare(b.command));
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
