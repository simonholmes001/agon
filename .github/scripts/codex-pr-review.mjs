#!/usr/bin/env node

import fs from 'fs';
import process from 'process';
import {
  buildSystemPrompt,
  buildUserPrompt,
  isChangesetReleasePr,
  isDependabotPr,
  loadSkillRubrics,
} from './codex-pr-review-core.mjs';

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

if (isDependabotPr(pr, process.env.GITHUB_ACTOR || '')) {
  console.log('Dependabot PR detected; skipping Codex review by design.');
  process.exit(0);
}

if (isChangesetReleasePr(pr)) {
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

const rubrics = loadSkillRubrics(process.cwd());
const systemPrompt = buildSystemPrompt(rubrics);
const userPrompt = buildUserPrompt(pr, diff, maxDiffChars, diffTruncated);

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
