export function shouldPrintVersion(args) {
  return args.includes('--version') || args.includes('-v');
}

export function shouldSelfUpdate(args) {
  return args.includes('--self-update') || args[0] === 'self-update';
}
