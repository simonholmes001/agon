export function shouldPrintVersion(args) {
  return args.includes('--version') || args.includes('-v');
}
