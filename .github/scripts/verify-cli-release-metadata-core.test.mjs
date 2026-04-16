import assert from 'node:assert/strict';
import test from 'node:test';

import {
  normalizeVersion,
  validateLatestVersion,
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

