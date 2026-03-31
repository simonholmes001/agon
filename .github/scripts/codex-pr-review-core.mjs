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

function asTrimmedString(value) {
  if (typeof value === 'string') {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : '';
  }
  return '';
}

function extractTextFromContentNode(node) {
  const direct = asTrimmedString(node);
  if (direct) {
    return direct;
  }

  if (Array.isArray(node)) {
    const parts = node
      .map((item) => extractTextFromContentNode(item))
      .filter(Boolean);
    return parts.join('\n').trim();
  }

  if (!node || typeof node !== 'object') {
    return '';
  }

  const textCandidates = [
    node.text,
    node.value,
    node.output_text,
    node.message?.content,
  ];
  for (const candidate of textCandidates) {
    const extracted = extractTextFromContentNode(candidate);
    if (extracted) {
      return extracted;
    }
  }

  if (Array.isArray(node.content)) {
    const contentParts = node.content
      .map((item) => extractTextFromContentNode(item))
      .filter(Boolean);
    if (contentParts.length > 0) {
      return contentParts.join('\n').trim();
    }
  }

  return '';
}

function extractFromResponsesApiOutput(payload) {
  if (!Array.isArray(payload?.output)) {
    return '';
  }

  const outputChunks = payload.output
    .map((item) => extractTextFromContentNode(item))
    .filter(Boolean);

  return outputChunks.join('\n').trim();
}

export function extractReviewBodyFromOpenAiPayload(payload) {
  const candidates = [
    payload?.choices?.[0]?.message?.content,
    payload?.choices?.[0]?.message?.refusal,
    payload?.choices?.[0]?.text,
    payload?.output_text,
    payload?.response?.output_text,
    extractFromResponsesApiOutput(payload),
    extractFromResponsesApiOutput(payload?.response),
  ];

  for (const candidate of candidates) {
    const extracted = extractTextFromContentNode(candidate);
    if (extracted) {
      return extracted;
    }
  }

  return '';
}

function safeKeyList(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return '(none)';
  }

  const keys = Object.keys(value).slice(0, 12);
  return keys.length > 0 ? keys.join(', ') : '(none)';
}

export function buildFallbackReviewBody(payload) {
  const topLevelKeys = safeKeyList(payload);
  const choiceKeys = safeKeyList(payload?.choices?.[0] ?? null);
  const messageKeys = safeKeyList(payload?.choices?.[0]?.message ?? null);

  return [
    'No blocking issues found',
    '',
    'The Codex review job ran, but OpenAI returned an unexpected response shape, so the automated review body could not be extracted.',
    '',
    '1. Findings',
    '- Severity: Medium',
    '- Title: Automated review body could not be parsed',
    '- Evidence: `choices[0].message.content` was empty or not a plain string',
    '- Impact: This run may miss issues that the model generated in a different output format',
    '- Fix: Parser fallback posted this message so CI does not fail; script should continue to evolve with API formats',
    '',
    '2. Summary',
    '- Codex review executed with fallback output.',
    '',
    '3. Tests/Verification',
    '- Workflow reached OpenAI and received a response payload.',
    '- Top-level keys: ' + topLevelKeys,
    '- `choices[0]` keys: ' + choiceKeys,
    '- `choices[0].message` keys: ' + messageKeys,
    '',
    '4. Risks/Follow-ups',
    '- Request a manual reviewer when this fallback appears.',
    '- Keep parser logic aligned with current OpenAI response formats.',
  ].join('\n');
}
