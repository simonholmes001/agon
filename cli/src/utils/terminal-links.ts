const ESC = '\u001B';
const OSC = `${ESC}]8;;`;
const BEL = '\u0007';
const ST = `${ESC}\\`;

type TerminalLinkOptions = {
  force?: boolean;
};

export function formatTerminalLink(url: string, label?: string, options: TerminalLinkOptions = {}): string {
  const text = label ?? url;
  if (!supportsHyperlinks(options.force ?? false)) {
    return text;
  }

  const terminator = resolveOscTerminator();
  return `${OSC}${url}${terminator}${text}${OSC}${terminator}`;
}

function supportsHyperlinks(force: boolean): boolean {
  if (force) {
    return process.stdout.isTTY;
  }

  if (!process.stdout.isTTY) {
    return false;
  }
  const term = (process.env.TERM ?? '').toLowerCase();
  if (!term || term === 'dumb') {
    return false;
  }
  if (process.env.TERM_NO_HYPERLINKS === '1') {
    return false;
  }
  return true;
}

function resolveOscTerminator(): string {
  // Apple Terminal consistently recognizes OSC8 links with ST terminators.
  if ((process.env.TERM_PROGRAM ?? '').toLowerCase() === 'apple_terminal') {
    return ST;
  }

  return BEL;
}
