import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import {
  buildSystemPrompt,
  buildUserPrompt,
  isChangesetReleasePr,
  isDependabotPr,
  loadSkillRubrics,
} from './codex-pr-review-core.mjs';

test('isDependabotPr returns true for dependabot branch', () => {
  const pr = {
    user: { login: 'octocat' },
    head: { ref: 'dependabot/npm_and_yarn/typescript-6.0.2' },
  };
  assert.equal(isDependabotPr(pr, ''), true);
});

test('isChangesetReleasePr returns true for changeset-release branch', () => {
  const pr = {
    user: { login: 'github-actions[bot]' },
    head: { ref: 'changeset-release/main' },
    title: 'Version Packages',
  };
  assert.equal(isChangesetReleasePr(pr), true);
});

test('loadSkillRubrics reads rubric files from repository-like tree', () => {
  const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'codex-review-rubrics-'));
  const rubricDir = path.join(tempRoot, '.github', 'review-rubrics');
  fs.mkdirSync(rubricDir, { recursive: true });
  fs.writeFileSync(path.join(rubricDir, 'pull-request-review.md'), 'pull request rubric');
  fs.writeFileSync(path.join(rubricDir, 'clean-code.md'), 'clean code rubric');
  fs.writeFileSync(path.join(rubricDir, 'clean-architecture.md'), 'clean architecture rubric');

  const rubrics = loadSkillRubrics(tempRoot);
  assert.equal(rubrics.pullRequestReview, 'pull request rubric');
  assert.equal(rubrics.cleanCode, 'clean code rubric');
  assert.equal(rubrics.cleanArchitecture, 'clean architecture rubric');
});

test('buildSystemPrompt includes all rubric sections', () => {
  const prompt = buildSystemPrompt({
    pullRequestReview: 'A',
    cleanCode: 'B',
    cleanArchitecture: 'C',
  });

  assert.match(prompt, /RUBRIC 1: pull-request-review/);
  assert.match(prompt, /RUBRIC 2: clean-code/);
  assert.match(prompt, /RUBRIC 3: clean-architecture/);
  assert.match(prompt, /A/);
  assert.match(prompt, /B/);
  assert.match(prompt, /C/);
});

test('buildUserPrompt includes truncation note when diff was truncated', () => {
  const pr = {
    title: 'T',
    user: { login: 'u' },
    base: { ref: 'main' },
    head: { ref: 'feature/x' },
    body: 'desc',
  };
  const prompt = buildUserPrompt(pr, 'diff', 123, true);
  assert.match(prompt, /Diff truncated at 123 characters/);
});

