#!/usr/bin/env node

import fs from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  fetchRegistryLatestVersion,
  verifyCliReleaseMetadataCore
} from './verify-cli-release-metadata-core.mjs';

const execFileAsync = promisify(execFile);
const scriptFilePath = fileURLToPath(import.meta.url);
const scriptsDirectoryPath = path.dirname(scriptFilePath);
const repositoryRootPath = path.resolve(scriptsDirectoryPath, '..', '..');
const cliPackageJsonPath = path.join(repositoryRootPath, 'cli', 'package.json');

export async function verifyCliReleaseMetadata() {
  await verifyCliReleaseMetadataCore({
    packageJsonPath: cliPackageJsonPath,
    readFileFn: fs.readFile,
    fetchLatestVersionFn: (packageName) =>
      fetchRegistryLatestVersion({
        packageName,
        execFileAsyncFn: execFileAsync
      })
  });
}

verifyCliReleaseMetadata().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
