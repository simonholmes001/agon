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
    'NOTES',
    `  Run ${binName} <command> --help for command-specific flags.`,
    `  Run ${binName} /help inside the interactive shell for slash commands.`
  ].join('\n');
}
