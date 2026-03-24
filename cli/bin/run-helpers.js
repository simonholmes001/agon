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
    { command: 'config [key] [value]', description: 'Display or modify configuration' },
    { command: 'keys <subcommand>', description: 'Manage provider API keys' },
    { command: 'login', description: 'Set up authentication for the backend' },
    { command: 'resume [session-id]', description: 'Resume a session and set it as current' },
    { command: 'self-update [--check]', description: 'Update CLI to latest version' },
    { command: 'sessions', description: 'List cached sessions' },
    { command: 'shell', description: 'Open interactive codex-style shell (default)' },
    { command: 'show <artifact>', description: 'Display an artifact from current session' },
    { command: 'start <idea>', description: 'Start a new strategy debate session' },
    { command: 'status [session-id]', description: 'Show current session status' }
  ];
  const commandLines = commandRows
    .sort((a, b) => a.command.localeCompare(b.command))
    .map(({ command, description }) => `  ${binName} ${command}`.padEnd(34) + description);

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
