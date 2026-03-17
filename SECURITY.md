# Security Policy

## Supported Versions

The following versions of `@agon_agents/cli` are currently supported with security updates:

| Version | Supported          |
| ------- | ------------------ |
| latest  | :white_check_mark: |

We support the latest published version only. Please update to the latest version before reporting a vulnerability.

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability in this project, please follow responsible disclosure:

**Do not open a public GitHub issue for security vulnerabilities.**

### How to Report

Please report security vulnerabilities by one of the following methods:

1. **GitHub Private Vulnerability Reporting (preferred):**  
   Use GitHub's built-in [private vulnerability reporting](https://github.com/simonholmes001/agon/security/advisories/new).

2. **Email:**  
   Send a detailed report to the repository maintainer via the contact information on their [GitHub profile](https://github.com/simonholmes001).

### What to Include

To help us triage and fix the issue quickly, please include:

- A clear description of the vulnerability
- Steps to reproduce the issue
- The potential impact (e.g., data exposure, code execution, privilege escalation)
- Affected versions
- Any suggested mitigations or fixes you may have identified

### What to Expect

- We will acknowledge your report within **5 business days**.
- We aim to provide an initial assessment within **10 business days**.
- We will keep you informed of our progress as we work on a fix.
- Once the vulnerability is confirmed and a fix is ready, we will coordinate the public disclosure with you.
- We will credit you in the release notes and security advisory (unless you prefer to remain anonymous).

## Security Update Process

When a security vulnerability is fixed:

1. A patched version is published to npm promptly.
2. A [GitHub Security Advisory](https://github.com/simonholmes001/agon/security/advisories) is published with details.
3. The CHANGELOG is updated with a note about the security fix.

## Scope

This security policy covers the `@agon_agents/cli` npm package and the source code in this repository.

Out of scope:
- Vulnerabilities in upstream dependencies (please report those to the respective maintainers)
- Issues in infrastructure or hosted services not covered by this repository

## Security Best Practices for Users

- Always install the latest version: `npm install -g @agon_agents/cli@latest`
- Verify package integrity via [npm provenance](https://docs.npmjs.com/generating-provenance-statements) — attestations are published for every release
- Review the [npm audit](https://docs.npmjs.com/cli/v8/commands/npm-audit) report for your own projects that depend on this package
