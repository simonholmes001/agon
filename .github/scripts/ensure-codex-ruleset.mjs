#!/usr/bin/env node

import process from 'process';

const token = process.env.REPO_ADMIN_TOKEN || '';
if (!token) {
  console.error('REPO_ADMIN_TOKEN is required to manage repository rulesets.');
  process.exit(1);
}

const repoSlug = process.env.GITHUB_REPOSITORY || '';
const [owner, repo] = repoSlug.split('/');
if (!owner || !repo) {
  console.error('GITHUB_REPOSITORY is not set or invalid.');
  process.exit(1);
}

const githubApi = process.env.GITHUB_API_URL || 'https://api.github.com';
const rulesetName = process.env.CODEX_RULESET_NAME || 'Require Codex Review';
const defaultRequiredCheck = process.env.CODEX_REVIEW_CHECK || 'Codex Review';
const targetBranch = process.env.CODEX_RULESET_BRANCH || 'refs/heads/main';
const bypassRepositoryRoleId = Number.parseInt(
  process.env.CODEX_BYPASS_REPOSITORY_ROLE_ID || '2',
  10,
);
const requiredChecks = resolveRequiredChecks();

function resolveRequiredChecks() {
  const csv = process.env.CODEX_REQUIRED_CHECKS || '';
  const checks = csv
    .split(',')
    .map((entry) => entry.trim())
    .filter(Boolean);

  if (checks.length > 0) {
    return Array.from(new Set(checks));
  }

  return [defaultRequiredCheck];
}

function ensureBypassActors(existingBypassActors = []) {
  const actors = Array.isArray(existingBypassActors) ? [...existingBypassActors] : [];
  const hasRepositoryRoleBypass = actors.some(
    (actor) =>
      actor?.actor_type === 'RepositoryRole' &&
      Number(actor?.actor_id) === bypassRepositoryRoleId &&
      actor?.bypass_mode === 'always',
  );

  if (!hasRepositoryRoleBypass) {
    actors.push({
      actor_id: bypassRepositoryRoleId,
      actor_type: 'RepositoryRole',
      bypass_mode: 'always',
    });
  }

  return actors;
}

async function githubRequest(path, options = {}) {
  const response = await fetch(`${githubApi}${path}`, {
    ...options,
    headers: {
      Authorization: `Bearer ${token}`,
      'User-Agent': 'codex-ruleset-automation',
      Accept: 'application/vnd.github+json',
      ...options.headers,
    },
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`GitHub API ${response.status} ${response.statusText}: ${text}`);
  }

  return response;
}

function ensureRequiredCheckRule(ruleset) {
  const existingRules = Array.isArray(ruleset.rules) ? ruleset.rules : [];
  const targetRule = existingRules.find((rule) => rule.type === 'required_status_checks');

  if (!targetRule) {
    return [
      ...existingRules,
      {
        type: 'required_status_checks',
        parameters: {
          required_status_checks: requiredChecks.map((context) => ({ context })),
          strict_required_status_checks_policy: false,
        },
      },
    ];
  }

  const existingChecks = Array.isArray(targetRule.parameters?.required_status_checks)
    ? targetRule.parameters.required_status_checks
    : [];

  const existingContexts = new Set(existingChecks.map((check) => check.context).filter(Boolean));
  const missingChecks = requiredChecks.filter((context) => !existingContexts.has(context));

  if (missingChecks.length === 0) {
    return existingRules;
  }

  const updatedRule = {
    ...targetRule,
    parameters: {
      ...targetRule.parameters,
      required_status_checks: [
        ...existingChecks,
        ...missingChecks.map((context) => ({ context })),
      ],
      strict_required_status_checks_policy: targetRule.parameters?.strict_required_status_checks_policy ?? false,
    },
  };

  return existingRules.map((rule) => (rule.type === 'required_status_checks' ? updatedRule : rule));
}

const rulesetResponse = await githubRequest(`/repos/${owner}/${repo}/rulesets?per_page=100`);
const rulesets = await rulesetResponse.json();
const existing = Array.isArray(rulesets)
  ? rulesets.find((ruleset) => ruleset.name === rulesetName)
  : null;

if (!existing) {
  const payload = {
    name: rulesetName,
    target: 'branch',
    enforcement: 'active',
    conditions: {
      ref_name: {
        include: [targetBranch],
        exclude: [],
      },
    },
    rules: [
      {
        type: 'required_status_checks',
        parameters: {
          required_status_checks: requiredChecks.map((context) => ({ context })),
          strict_required_status_checks_policy: false,
        },
      },
    ],
    bypass_actors: ensureBypassActors(),
  };

  await githubRequest(`/repos/${owner}/${repo}/rulesets`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(payload),
  });

  console.log(`Created ruleset "${rulesetName}" with required checks: ${requiredChecks.join(', ')}.`);
  process.exit(0);
}

const rulesetDetailsResponse = await githubRequest(`/repos/${owner}/${repo}/rulesets/${existing.id}`);
const rulesetDetails = await rulesetDetailsResponse.json();

const updatedRules = ensureRequiredCheckRule(rulesetDetails);
const updatePayload = {
  name: rulesetDetails.name,
  target: rulesetDetails.target,
  enforcement: rulesetDetails.enforcement,
  conditions: rulesetDetails.conditions,
  rules: updatedRules,
  bypass_actors: ensureBypassActors(rulesetDetails.bypass_actors || []),
};

await githubRequest(`/repos/${owner}/${repo}/rulesets/${existing.id}`, {
  method: 'PUT',
  headers: {
    'Content-Type': 'application/json',
  },
  body: JSON.stringify(updatePayload),
});

console.log(`Updated ruleset "${rulesetName}" to require checks: ${requiredChecks.join(', ')}.`);
