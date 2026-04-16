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

export function parseCliPackageMetadata(rawPackageJson, sourcePath) {
  const parsed = JSON.parse(rawPackageJson);
  const packageName = normalizeVersion(parsed.name);
  const packageVersion = normalizeVersion(parsed.version);

  if (!packageName) {
    throw new Error(`Missing package name in ${sourcePath}.`);
  }

  if (!packageVersion) {
    throw new Error(`Missing package version in ${sourcePath}.`);
  }

  return { packageName, packageVersion };
}

export async function fetchRegistryLatestVersion({ packageName, execFileAsyncFn }) {
  try {
    const { stdout } = await execFileAsyncFn('npm', ['view', `${packageName}@latest`, 'version', '--silent']);
    return normalizeVersion(stdout);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`Unable to read npm latest for ${packageName}: ${message}`);
  }
}

export async function verifyCliReleaseMetadataCore({
  packageJsonPath,
  readFileFn,
  fetchLatestVersionFn,
  logFn = console.log
}) {
  const packageJsonRaw = await readFileFn(packageJsonPath, 'utf8');
  const { packageName, packageVersion } = parseCliPackageMetadata(packageJsonRaw, packageJsonPath);
  const latestVersion = await fetchLatestVersionFn(packageName);

  validateLatestVersion({
    expectedVersion: packageVersion,
    latestVersion
  });

  logFn(`Verified npm latest for ${packageName}: ${latestVersion}`);
}
