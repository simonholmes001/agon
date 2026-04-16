#!/usr/bin/env node

import fs from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { normalizeVersion, validateLatestVersion } from './verify-cli-release-metadata-core.mjs';

const execFileAsync = promisify(execFile);

async function readCliPackageMetadata(packageJsonPath) {
  const raw = await fs.readFile(packageJsonPath, 'utf8');
  const parsed = JSON.parse(raw);

  const packageName = normalizeVersion(parsed.name);
  const packageVersion = normalizeVersion(parsed.version);

  if (!packageName) {
    throw new Error(`Missing package name in ${packageJsonPath}.`);
  }

  if (!packageVersion) {
    throw new Error(`Missing package version in ${packageJsonPath}.`);
  }

  return { packageName, packageVersion };
}

async function fetchRegistryLatestVersion(packageName) {
  const { stdout } = await execFileAsync('npm', ['view', `${packageName}@latest`, 'version', '--silent']);
  return normalizeVersion(stdout);
}

export async function verifyCliReleaseMetadata() {
  const { packageName, packageVersion } = await readCliPackageMetadata('cli/package.json');
  const latestVersion = await fetchRegistryLatestVersion(packageName);

  validateLatestVersion({
    expectedVersion: packageVersion,
    latestVersion
  });

  console.log(`Verified npm latest for ${packageName}: ${latestVersion}`);
}

verifyCliReleaseMetadata().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});

