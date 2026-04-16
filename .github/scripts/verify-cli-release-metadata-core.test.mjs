import assert from 'node:assert/strict';
import test from 'node:test';

import {
  fetchRegistryLatestVersion,
  normalizeVersion,
  parseCliPackageMetadata,
  validateLatestVersion,
  verifyCliReleaseMetadataCore
} from './verify-cli-release-metadata-core.mjs';

test('normalizeVersion trims whitespace and line breaks', () => {
  assert.equal(normalizeVersion(' 0.9.1 \n'), '0.9.1');
});

test('validateLatestVersion accepts matching expected/latest versions', () => {
  assert.doesNotThrow(() => validateLatestVersion({ expectedVersion: '0.9.1', latestVersion: '0.9.1' }));
});

test('validateLatestVersion throws when latest does not match expected', () => {
  assert.throws(
    () => validateLatestVersion({ expectedVersion: '0.9.1', latestVersion: '0.9.0' }),
    /Registry latest version mismatch/
  );
});

test('parseCliPackageMetadata reads package name/version from package.json payload', () => {
  const result = parseCliPackageMetadata(
    JSON.stringify({ name: '@agon_agents/cli', version: '0.9.1' }),
    '/repo/cli/package.json'
  );

  assert.deepEqual(result, { packageName: '@agon_agents/cli', packageVersion: '0.9.1' });
});

test('fetchRegistryLatestVersion wraps npm lookup failures with actionable context', async () => {
  await assert.rejects(
    () =>
      fetchRegistryLatestVersion({
        packageName: '@agon_agents/cli',
        execFileAsyncFn: async () => {
          throw new Error('403 unauthorized');
        }
      }),
    /Unable to read npm latest for @agon_agents\/cli: 403 unauthorized/
  );
});

test('verifyCliReleaseMetadataCore verifies and logs when versions match', async () => {
  const logs = [];

  await verifyCliReleaseMetadataCore({
    packageJsonPath: '/repo/cli/package.json',
    readFileFn: async () => JSON.stringify({ name: '@agon_agents/cli', version: '0.9.1' }),
    fetchLatestVersionFn: async () => '0.9.1',
    logFn: (line) => logs.push(line)
  });

  assert.equal(logs.length, 1);
  assert.match(logs[0], /Verified npm latest for @agon_agents\/cli: 0.9.1/);
});
