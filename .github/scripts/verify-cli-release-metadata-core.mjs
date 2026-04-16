export function normalizeVersion(rawValue) {
  return String(rawValue ?? '').trim();
}

export function validateLatestVersion({ expectedVersion, latestVersion }) {
  if (!expectedVersion) {
    throw new Error('Expected version is required.');
  }

  if (!latestVersion) {
    throw new Error('Registry latest version is empty.');
  }

  if (latestVersion !== expectedVersion) {
    throw new Error(
      `Registry latest version mismatch. Expected ${expectedVersion}, got ${latestVersion}.`
    );
  }
}

