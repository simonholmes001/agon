#!/usr/bin/env node

import fs from 'fs';
import process from 'process';

const eventPath = process.env.GITHUB_EVENT_PATH;
if (!eventPath) {
  console.error('GITHUB_EVENT_PATH is not set.');
  process.exit(1);
}

const event = JSON.parse(fs.readFileSync(eventPath, 'utf8'));
const pr = event.pull_request;
if (!pr) {
  console.error('This workflow must run on a pull_request or pull_request_target event.');
  process.exit(1);
}

const prAuthor = pr.user?.login || '';
const headRef = pr.head?.ref || '';
const isDependabotPr =
  prAuthor === 'dependabot[bot]' ||
  process.env.GITHUB_ACTOR === 'dependabot[bot]' ||
  headRef.startsWith('dependabot/');

const isChangesetReleasePr =
  headRef.startsWith('changeset-release/') ||
  (prAuthor === 'github-actions[bot]' && /^Version Packages/i.test(pr.title || ''));

if (isDependabotPr) {
  console.log('Dependabot PR detected; skipping Codex review by design.');
  process.exit(0);
}

if (isChangesetReleasePr) {
  console.log('Changeset release PR detected; bypassing Codex review by design.');
  process.exit(0);
}

const repoSlug = process.env.GITHUB_REPOSITORY || '';
const [owner, repo] = repoSlug.split('/');
if (!owner || !repo) {
  console.error('GITHUB_REPOSITORY is not set or invalid.');
  process.exit(1);
}

const githubToken = process.env.GITHUB_TOKEN;
if (!githubToken) {
  console.error('GITHUB_TOKEN is not set.');
  process.exit(1);
}

const openAiKey = process.env.OPENAI_KEY || process.env.OPENAI_API_KEY;
if (!openAiKey) {
  console.error('OPENAI_KEY (or OPENAI_API_KEY) is not set.');
  process.exit(1);
}

const githubApi = process.env.GITHUB_API_URL || 'https://api.github.com';
const model = process.env.CODEX_REVIEW_MODEL || 'gpt-5.2';
const maxDiffChars = Number.parseInt(process.env.CODEX_REVIEW_DIFF_MAX || '120000', 10);

async function githubRequest(path, options = {}) {
  const response = await fetch(`${githubApi}${path}`, {
    ...options,
    headers: {
      Authorization: `Bearer ${githubToken}`,
      'User-Agent': 'codex-pr-review',
      ...options.headers,
    },
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`GitHub API ${response.status} ${response.statusText}: ${text}`);
  }

  return response;
}

const diffResponse = await githubRequest(`/repos/${owner}/${repo}/pulls/${pr.number}`,
  {
    headers: {
      Accept: 'application/vnd.github.v3.diff',
    },
  });

let diff = await diffResponse.text();
let diffTruncated = false;
if (diff.length > maxDiffChars) {
  diff = diff.slice(0, maxDiffChars);
  diffTruncated = true;
}

const prInfo = [
  `Title: ${pr.title}`,
  `Author: ${pr.user?.login || 'unknown'}`,
  `Base: ${pr.base?.ref || ''}`,
  `Head: ${pr.head?.ref || ''}`,
  'Body:',
  pr.body || '(no description)',
].join('\n');

const truncationNote = diffTruncated
  ? `\n[Diff truncated at ${maxDiffChars} characters. Focus on highest-risk changes first.]`
  : '';

const systemPrompt = [
  'You are Codex performing a rigorous PR review.',
  'Apply three rubrics in this order:',
  '1) pull-request-review: risk-based, behavior-first, findings ordered by severity, include evidence and fixes.',
  '2) clean-code: clarity, naming, error handling, readability, and test quality.',
  '3) clean-architecture: boundaries, dependency direction, adapter separation, and policy isolation.',
  'Do not approve speculative issues without evidence in the diff.',
  'If no issues found, say "No blocking issues found" and list residual risks/testing gaps.',
].join(' ');

const userPrompt = [
  'Review the following PR diff.',
  '',
  prInfo,
  '',
  'Diff:',
  diff,
  truncationNote,
  '',
  'Respond in Markdown with:',
  '1. Findings (each with Severity, Title, Evidence, Impact, Fix)',
  '2. Summary (short)',
  '3. Tests/Verification (what was run / missing)',
  '4. Risks/Follow-ups (if any)',
].join('\n');

const openAiResponse = await fetch('https://api.openai.com/v1/chat/completions', {
  method: 'POST',
  headers: {
    Authorization: `Bearer ${openAiKey}`,
    'Content-Type': 'application/json',
  },
  body: JSON.stringify({
    model,
    temperature: 0.2,
    max_completion_tokens: 1500,
    messages: [
      { role: 'system', content: systemPrompt },
      { role: 'user', content: userPrompt },
    ],
  }),
});

if (!openAiResponse.ok) {
  const text = await openAiResponse.text();
  throw new Error(`OpenAI API ${openAiResponse.status} ${openAiResponse.statusText}: ${text}`);
}

const openAiPayload = await openAiResponse.json();
const reviewBody = openAiPayload?.choices?.[0]?.message?.content?.trim();
if (!reviewBody) {
  throw new Error('OpenAI response did not include a review body.');
}

const taggedBody = `<!-- codex-review -->\n${reviewBody}`;

await githubRequest(`/repos/${owner}/${repo}/pulls/${pr.number}/reviews`, {
  method: 'POST',
  headers: {
    Accept: 'application/vnd.github+json',
    'Content-Type': 'application/json',
  },
  body: JSON.stringify({
    body: taggedBody,
    event: 'COMMENT',
  }),
});

console.log('Codex review posted.');
