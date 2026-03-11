export interface CliUpdateInfo {
  packageName: string;
  currentVersion: string;
  latestVersion: string;
  installCommand: string;
}

interface FetchResponseLike {
  ok: boolean;
  json(): Promise<unknown>;
}

type FetchLike = (input: string, init?: { method?: string; signal?: AbortSignal }) => Promise<FetchResponseLike>;

export interface CheckForCliUpdateOptions {
  packageName: string;
  currentVersion: string;
  registryBaseUrl?: string;
  timeoutMs?: number;
  fetchFn?: FetchLike;
}

interface ParsedSemver {
  major: number;
  minor: number;
  patch: number;
  prerelease: string[];
}

const semverPattern = /^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$/;

export async function checkForCliUpdate(
  options: CheckForCliUpdateOptions
): Promise<CliUpdateInfo | null> {
  const registryBaseUrl = options.registryBaseUrl ?? 'https://registry.npmjs.org';
  const timeoutMs = options.timeoutMs ?? 1500;
  const fetchFn = options.fetchFn ?? globalThis.fetch;

  if (typeof fetchFn !== 'function') {
    return null;
  }

  const current = parseSemver(options.currentVersion);
  if (!current) {
    return null;
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  if (typeof timeout.unref === 'function') {
    timeout.unref();
  }

  try {
    const packagePath = encodeURIComponent(options.packageName);
    const trimmedBase = registryBaseUrl.replace(/\/+$/, '');
    const url = `${trimmedBase}/${packagePath}/latest`;

    const response = await fetchFn(url, {
      method: 'GET',
      signal: controller.signal
    });
    if (!response.ok) {
      return null;
    }

    const payload = await response.json();
    const latestVersion = getLatestVersion(payload);
    if (!latestVersion) {
      return null;
    }

    const latest = parseSemver(latestVersion);
    if (!latest || compareSemver(latest, current) <= 0) {
      return null;
    }

    return {
      packageName: options.packageName,
      currentVersion: options.currentVersion,
      latestVersion,
      installCommand: `npm install -g ${options.packageName}@latest`
    };
  } catch {
    return null;
  } finally {
    clearTimeout(timeout);
  }
}

function getLatestVersion(payload: unknown): string | null {
  if (!payload || typeof payload !== 'object') {
    return null;
  }

  const version = (payload as { version?: unknown }).version;
  return typeof version === 'string' ? version : null;
}

function parseSemver(version: string): ParsedSemver | null {
  const match = version.match(semverPattern);
  if (!match) {
    return null;
  }

  const prerelease = match[4] ? match[4].split('.') : [];
  return {
    major: Number(match[1]),
    minor: Number(match[2]),
    patch: Number(match[3]),
    prerelease
  };
}

function compareSemver(a: ParsedSemver, b: ParsedSemver): number {
  if (a.major !== b.major) return a.major > b.major ? 1 : -1;
  if (a.minor !== b.minor) return a.minor > b.minor ? 1 : -1;
  if (a.patch !== b.patch) return a.patch > b.patch ? 1 : -1;

  const aPre = a.prerelease;
  const bPre = b.prerelease;

  if (aPre.length === 0 && bPre.length === 0) return 0;
  if (aPre.length === 0) return 1;
  if (bPre.length === 0) return -1;

  const maxLength = Math.max(aPre.length, bPre.length);
  for (let index = 0; index < maxLength; index += 1) {
    const left = aPre[index];
    const right = bPre[index];

    if (left === undefined) return -1;
    if (right === undefined) return 1;

    const leftNumeric = /^\d+$/.test(left);
    const rightNumeric = /^\d+$/.test(right);

    if (leftNumeric && rightNumeric) {
      const leftNumber = Number(left);
      const rightNumber = Number(right);
      if (leftNumber !== rightNumber) {
        return leftNumber > rightNumber ? 1 : -1;
      }
      continue;
    }

    if (leftNumeric !== rightNumeric) {
      return leftNumeric ? -1 : 1;
    }

    if (left !== right) {
      return left > right ? 1 : -1;
    }
  }

  return 0;
}
