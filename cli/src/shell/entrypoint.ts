export function ensureDefaultShellCommand(argv: string[]): string[] {
  if (argv.length <= 2) {
    return [...argv, 'shell'];
  }

  return argv;
}
