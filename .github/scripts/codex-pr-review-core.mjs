#!/usr/bin/env node

import fs from 'fs';
import path from 'path';

const RUBRIC_FILE_MAP = {
  pullRequestReview: 'pull-request-review.md',
  cleanCode: 'clean-code.md',
  cleanArchitecture: 'clean-architecture.md',
};

export function isDependabotPr(pr, actor = '') {
  const author = pr?.user?.login || '';
  const headRef = pr?.head?.ref || '';
  return author === 'dependabot[bot]' || actor === 'dependabot[bot]' || headRef.startsWith('dependabot/');
}

export function isChangesetReleasePr(pr) {
  const author = pr?.user?.login || '';
  const headRef = pr?.head?.ref || '';
  const title = pr?.title || '';
  return headRef.startsWith('changeset-release/') || (author === 'github-actions[bot]' && /^Version Packages/i.test(title));
}

export function loadSkillRubrics(baseDir = process.cwd()) {
  const rubricDirectory = path.join(baseDir, '.github', 'review-rubrics');
  const rubrics = {};

  for (const [key, fileName] of Object.entries(RUBRIC_FILE_MAP)) {
    const filePath = path.join(rubricDirectory, fileName);
    if (!fs.existsSync(filePath)) {
      throw new Error(`Missing rubric file: ${filePath}`);
    }

    const content = fs.readFileSync(filePath, 'utf8').trim();
    if (!content) {
      throw new Error(`Empty rubric file: ${filePath}`);
    }

    rubrics[key] = content;
  }

  return rubrics;
}

export function buildSystemPrompt(rubrics) {
  return [
    'You are Codex performing a rigorous PR review.',
    'Treat the following rubric documents as mandatory instructions and apply them in this order.',
    'If rubrics conflict, prioritize security/correctness over style.',
    'Do not make speculative findings without evidence in the diff.',
    '',
    'RUBRIC 1: pull-request-review',
    rubrics.pullRequestReview,
    '',
    'RUBRIC 2: clean-code',
    rubrics.cleanCode,
    '',
    'RUBRIC 3: clean-architecture',
    rubrics.cleanArchitecture,
    '',
    'Output contract:',
    '- Findings first, ordered by severity (Critical, High, Medium, Low).',
    '- Each finding must include Severity, Title, Evidence, Impact, Fix.',
    '- If no findings: output "No blocking issues found" and list residual risks/testing gaps.',
  ].join('\n');
}

export function buildUserPrompt(pr, diff, maxDiffChars, diffTruncated) {
  const prInfo = [
    `Title: ${pr?.title || ''}`,
    `Author: ${pr?.user?.login || 'unknown'}`,
    `Base: ${pr?.base?.ref || ''}`,
    `Head: ${pr?.head?.ref || ''}`,
    'Body:',
    pr?.body || '(no description)',
  ].join('\n');

  const truncationNote = diffTruncated
    ? `\n[Diff truncated at ${maxDiffChars} characters. Focus on highest-risk changes first.]`
    : '';

  return [
    'Review the following pull request diff.',
    '',
    prInfo,
    '',
    'Diff:',
    diff,
    truncationNote,
  ].join('\n');
}

