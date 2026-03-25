const OSC = '\u001B]8;;';
const BEL = '\u0007';
const OSC_END = '\u001B]8;;\u0007';

export function formatTerminalLink(url: string, label?: string): string {
  const text = label ?? url;
  if (!supportsHyperlinks()) {
    return text;
  }
  return `${OSC}${url}${BEL}${text}${OSC_END}`;
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
