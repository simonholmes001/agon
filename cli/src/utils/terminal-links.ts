const OSC = '\u001B]8;;';
const OSC_END = '\u001B]8;;';
const BEL = '\u0007';
const ST = '\u001B\\';

export function formatTerminalLink(url: string, label?: string): string {
  const text = label ?? url;
  if (!supportsHyperlinks()) {
    return text;
  }
  const terminator = resolveOscTerminator();
  return `${OSC}${url}${terminator}${text}${OSC_END}${terminator}`;
}

function supportsHyperlinks(): boolean {
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
  const override = (process.env.AGON_HYPERLINK_TERMINATOR ?? '').toLowerCase();
  if (override === 'bel') {
    return BEL;
  }
  if (override === 'st') {
    return ST;
  }

  // macOS terminals have better interoperability with ST terminators.
  return process.platform === 'darwin' ? ST : BEL;
}
