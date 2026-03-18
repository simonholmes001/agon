# Agon

[![Next.js](https://img.shields.io/badge/Next.js-16-000000?style=flat-square&logo=next.js)](https://nextjs.org)
[![React](https://img.shields.io/badge/React-19-61DAFB?style=flat-square&logo=react&logoColor=000)](https://react.dev)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?style=flat-square&logo=typescript&logoColor=fff)](https://www.typescriptlang.org)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS-v4-06B6D4?style=flat-square&logo=tailwindcss&logoColor=fff)](https://tailwindcss.com)
[![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://dotnet.microsoft.com)
[![Vitest](https://img.shields.io/badge/Tested_with-Vitest-6E9F18?style=flat-square&logo=vitest&logoColor=fff)](https://vitest.dev)
[![xUnit](https://img.shields.io/badge/Tested_with-xUnit-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://xunit.net)
[![Tests](https://img.shields.io/badge/Tests-782_passing-brightgreen?style=flat-square)]()
[![Coverage](https://img.shields.io/badge/Coverage-36%25_lines-red?style=flat-square)]()
[![TDD](https://img.shields.io/badge/Methodology-TDD-red?style=flat-square)]()
[![Licence](https://img.shields.io/badge/Licence-Private-lightgrey?style=flat-square)]()
[![Deploy Infra Dev](https://github.com/simonholmes001/agon/actions/workflows/infrastructure-deploy-dev.yaml/badge.svg?branch=main)](https://github.com/simonholmes001/agon/actions/workflows/infrastructure-deploy-dev.yaml)
[![Deploy Backend Dev](https://github.com/simonholmes001/agon/actions/workflows/backend-deploy-dev.yaml/badge.svg?branch=main)](https://github.com/simonholmes001/agon/actions/workflows/backend-deploy-dev.yaml)
[![Deploy Preprod](https://img.shields.io/badge/Deploy%20Preprod-planned-lightgrey?style=flat-square)]()
[![Deploy Prod](https://img.shields.io/badge/Deploy%20Prod-planned-lightgrey?style=flat-square)]()

> Multi-agent AI analysis that transforms raw ideas into production-ready documentation.
>
> **Why the name "Agon"?**  
> *Agon* comes from the Greek **ἀγών** (*agōn*), meaning a contest, struggle, or formal debate.  
> The name fits this product because ideas are stress-tested through structured multi-agent debate before producing final execution artifacts.

---

## Table of Contents

- [What is Agon?](#project-overview)
- [How to Run Agon](#installation)
  - [CLI Application](#cli-application-available-now)
  - [Web Application](#web-application-in-development)
  - [iOS Application](#ios-application-in-development)
  - [Local Deployment (Developer Runbook)](#local-deployment-developer-runbook)
- [Repository Structure](#repository-structure)
- [Development Guide](#for-developers)
  - [CLI (TypeScript)](#cli-typescript)
  - [Backend (.NET)](#backend-net)
  - [Frontend (Next.js)](#frontend-nextjs)
- [Testing and Quality](#testing--quality)
  - [Running Tests](#running-tests)
- [CLI Release Process](#cli-release-process)
- [Architecture Documentation](#architecture-documentation)
- [Current Status](#current-status-march-2026)
- [Contributing to Agon](#contributing)
  - [Reporting Issues and Feature Requests](#reporting-issues-and-feature-requests)
  - [Submitting Pull Requests](#submitting-pull-requests)
- [License](#license)
- [Notes for Project Maintainers](#notes-for-project-maintainers)

---

## Project Overview

**Agon** transforms raw ideas into production-ready documentation through structured multi-agent analysis. You submit a product concept, technical proposal, strategic decision, or just a simple question, and a council of AI agents (powered by OpenAI GPT, Google Gemini, and Anthropic Claude) collaboratively analyze it across multiple rounds to produce:

- **Verdict** — Executive summary with go/no-go recommendation
- **30/60/90 Day Plan** — Phased implementation roadmap  
- **Product Requirements Document (PRD)** — Full specifications with acceptance criteria
- **Risk Registry** — Identified risks with severity, likelihood, and mitigation strategies
- **Assumption Validation** — Critical assumptions requiring validation before execution
- **Architecture Overview** — Technical design and system topology (Mermaid diagrams)
- **Copilot Instructions** — Implementation guidance for development

### How It Works

1. **Clarification Phase**: A Moderator agent asks clarifying questions to refine your idea into a structured "Debate Brief"
2. **Analysis Round**: Three council agents (GPT, Gemini, Claude) independently analyze your idea in parallel, each adding claims, assumptions, and risks to a shared **Truth Map**
3. **Critique Round**: Each agent critiques the other two agents' work, challenging assumptions and refining confidence scores
4. **Synthesis**: A Synthesizer agent unifies all perspectives into coherent, decision-grade artifacts
5. **Post-Delivery Follow-Up**: Continue the conversation — ask questions, challenge claims, or request revisions through an interactive shell

The **Truth Map** is the authoritative session state — a structured graph of claims, assumptions, risks, and decisions with full provenance tracking. All artifacts are generated from this map, never from raw conversation transcripts. If constraints change mid-session, the system automatically recalculates impact and agents reevaluate affected claims.

---

## Installation

Agon can be accessed through multiple interfaces. The CLI is currently available, with web and mobile applications in development.

### CLI Application (Available Now)

The Agon CLI is distributed as an npm package: `@agon_agents/cli`.

#### Prerequisites

- **Node.js 20+** ([download](https://nodejs.org/))
- **npm** (included with Node.js)

#### Installation Methods (Global, npx, Local)

##### 1) Global Installation (Recommended for daily use)

Install globally to run `agon` from any terminal directory:

```bash
npm install -g @agon_agents/cli
```

Verify installed version:

```bash
agon --version
```

Launch shell:

```bash
agon
```

Inside the shell, run:

```bash
/help
```

Update global install to latest stable:

```bash
npm install -g @agon_agents/cli@latest
```

Advantages:
- `agon` works everywhere on your machine
- Best startup speed for frequent use

Disadvantages:
- Requires global npm install permissions
- Version is shared across all projects on your machine

##### 2) npx (No permanent install)

Run without a global install:

```bash
npx @agon_agents/cli
```

Check version:

```bash
npx @agon_agents/cli --version
```

You can force the newest release explicitly:

```bash
npx @agon_agents/cli@latest
```

Advantages:
- No global install required
- Easy one-off usage

Disadvantages:
- Usually slower startup than global install
- Can depend on registry/network availability when not cached

##### 3) Local Project Installation (version-pinned per repo)

Install into your project:

```bash
npm install --save-dev @agon_agents/cli
```

Add script in `package.json`:

```json
{
  "scripts": {
    "agon": "agon"
  }
}
```

Run:

```bash
npm run agon
```

Check local installed version:

```bash
npm exec -- agon --version
```

Advantages:
- Version is pinned in project lockfile (reproducible team setup)
- No global dependency required

Disadvantages:
- Not directly available as `agon` outside that project
- Usually invoked via `npm run` / `npm exec`

#### Uninstalling

Remove global installation:

```bash
npm uninstall -g @agon_agents/cli
```

Remove local project installation:

```bash
npm uninstall @agon_agents/cli
```

#### Quick Start (Current Shell-First UX)

After launching `agon`, use these in-shell commands:

```bash
/help
/new
/show-sessions
/resume [session-id]
/session <session-id>
/status [session-id]
/status
/show verdict
/refresh verdict
/attach "./docs/product-brief.pdf"
/follow-up "<your request>"
/self-update [--check]
/exit
```

Notes:
- `agon --version` prints the installed CLI version and exits.
- `agon --help` shows launcher help.
- `agon --self-update` updates the global CLI install from terminal.
- `/self-update` runs the same update flow from inside an active shell session.
- By default, Agon CLI connects to the hosted backend endpoint (no manual `apiUrl` setup required for end users).
- After successful in-shell update, your current session remains usable; restart later to run the newly installed runtime.
- On startup, Agon checks npm and alerts when a newer stable version is available.

### Web Application (In Development)

Browser-based interface with visual Truth Map explorer and real-time agent streaming. Coming soon.

### iOS Application (In Development)

Native iOS app for on-the-go strategy analysis. Coming soon.

### Local Deployment (Developer Runbook)

For local end-to-end testing, run the data services first, then the backend API, then your chosen client interface.

#### 0) Build locally (repo root)

```bash
cd /Users/simonholmes/Projects/Applications/Agon
dotnet build backend/Agon.sln
npm --prefix cli run build
```

#### 1) Start required local data services (PostgreSQL + Redis)

```bash
cd backend
docker compose up -d postgres redis
```

Optional (DB admin UI):

```bash
cd backend
docker compose --profile tools up -d pgadmin
```

#### 2) Run backend API locally

```bash
cd backend
dotnet run --project src/Agon.Api/Agon.Api.csproj
```

For local backend testing, point the CLI to your local API process:

```bash
AGON_API_URL=http://localhost:5000 npm exec -- agon
```

#### 3) Run a local client

CLI (primary interface today, using local source build):

```bash
cd /Users/simonholmes/Projects/Applications/Agon
npm --prefix cli run build
AGON_API_URL=http://localhost:5000 node cli/bin/run.js
```

CLI (alternative, npm-exec from local package):

```bash
cd /Users/simonholmes/Projects/Applications/Agon/cli
npm install
AGON_API_URL=http://localhost:5000 npm exec -- agon
```

Optional auth token for secured API (JWT bearer):

```bash
AGON_AUTH_TOKEN="<jwt-token>" AGON_API_URL=http://localhost:5000 node cli/bin/run.js
```

Web frontend (WIP):

```bash
cd frontend
npm install
npm run dev
```

Frontend default URL: `http://localhost:3000`

#### 4) Stop local data services

```bash
cd backend
docker compose down
```

#### 5) Run local tests (repo root)

```bash
cd /Users/simonholmes/Projects/Applications/Agon
DOTNET_CLI_HOME=/tmp dotnet test backend/Agon.sln --verbosity minimal
npm --prefix cli test
```

---

## Repository Structure

```
Agon/
├── backend/              # .NET 9 backend
│   ├── src/
│   │   ├── Agon.Domain/              # Pure business logic
│   │   ├── Agon.Application/         # Orchestration & use-cases
│   │   ├── Agon.Infrastructure/      # Persistence, MAF, SignalR
│   │   └── Agon.Api/                 # REST + SignalR host
│   ├── tests/
│   │   ├── Agon.Domain.Tests/
│   │   ├── Agon.Application.Tests/
│   │   ├── Agon.Infrastructure.Tests/
│   │   └── Agon.Integration.Tests/
│   └── Agon.sln
│
├── cli/                  # TypeScript oclif CLI + interactive shell
│   ├── src/
│   │   ├── commands/                 # oclif commands (start, show, status, etc.)
│   │   ├── shell/                    # Interactive shell engine
│   │   ├── api/                      # Backend API client
│   │   ├── state/                    # Local session/config management
│   │   ├── ui/                       # Terminal UI components
│   │   └── utils/                    # Logger, formatters, error handling
│   ├── test/                         # Vitest unit tests
│   └── package.json
│
├── frontend/             # Next.js App Router frontend (scaffold/WIP)
│
└── .github/
    ├── workflows/        # CI + badge automation
    ├── scripts/          # Badge update script
    └── instructions/     # Architecture & coding rules
```

---

## For Developers

This section is for developers working on the Agon codebase.

### Prerequisites

- **.NET 9.0 SDK** ([download](https://dotnet.microsoft.com/download))
- **Node.js 20+** ([download](https://nodejs.org/))
- **PostgreSQL 16+** (for production; tests use in-memory DB)
- **Redis 7+** (for production; tests use mocked client)

---

## CLI (TypeScript)

### Architecture

The CLI is built with **oclif** (Open CLI Framework) and provides both command-driven and interactive shell modes.

**Key components:**

- **Commands** (`src/commands/`) - oclif command classes for `start`, `show`, `status`, `sessions`, `resume`, `config`, `answer`
- **Interactive Shell** (`src/shell/`) - Full-featured REPL with slash commands, history, and streaming output
- **API Client** (`src/api/`) - REST client wrapper for backend communication with retry logic and error handling
- **State Management** (`src/state/`) - Local session cache and config management (`~/.agon/`)
- **UI Components** (`src/ui/`) - Terminal renderers for Markdown, progress indicators, and formatted output
- **Utilities** (`src/utils/`) - Logger, error formatter, session flow helpers

### Running Tests

```bash
cd cli
npm test
```

### Test Coverage

CLI test projects use **Vitest**. Coverage is tracked in `cli/coverage/coverage-summary.json` and included in the combined badge at the top of this README.

### Key Technologies

- **oclif 3.0** - CLI framework with plugin architecture
- **inquirer** - Interactive prompts for clarification Q&A
- **ora** - Terminal spinners for async operations
- **chalk** - Terminal colors and styling
- **marked-terminal** - Markdown rendering in terminal
- **axios** - HTTP client for API communication
- **Vitest** - Unit testing framework

---

## Backend (.NET)

### Architecture

The backend follows **Clean Architecture** with strict layer separation:

```
┌─────────────────────────────────────┐
│         API Layer                    │  ← HTTP endpoints, middleware
│  ┌───────────────────────────────┐  │
│  │   Infrastructure Layer        │  │  ← Database, SignalR, MAF, Redis
│  │  ┌─────────────────────────┐  │  │
│  │  │  Application Layer      │  │  │  ← Orchestration, use-cases
│  │  │  ┌───────────────────┐  │  │  │
│  │  │  │  Domain Layer     │  │  │  │  ← Pure business logic
│  │  │  │                   │  │  │  │
│  │  │  └───────────────────┘  │  │  │
│  │  └─────────────────────────┘  │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
```

**Key principle:** Dependencies point **inward only**. Domain has zero external dependencies.

### Running Tests

```bash
cd backend
dotnet test
```

**Current status:** See the `Tests` badge at the top of this README (auto-updated on `main`).

### Test Coverage

Coverage and aggregate test counts are maintained by CI badges at the top of this README.

Backend test projects:

- `backend/tests/Agon.Domain.Tests`
- `backend/tests/Agon.Application.Tests`
- `backend/tests/Agon.Infrastructure.Tests`
- `backend/tests/Agon.Integration.Tests`

### Key Technologies

- **EF Core 9.0** with PostgreSQL provider (Npgsql)
- **Microsoft Agent Framework (MAF)** via `Microsoft.Extensions.AI`
- **StackExchange.Redis 2.11.8** for snapshot storage
- **SignalR** for real-time UI updates
- **xUnit + FluentAssertions + NSubstitute** for testing

### NuGet Packages

```bash
# Domain (no external dependencies)
dotnet add src/Agon.Domain/Agon.Domain.csproj package <none>

# Application (interfaces only)
dotnet add src/Agon.Application/Agon.Application.csproj package Microsoft.Extensions.AI

# Infrastructure (all I/O)
dotnet add src/Agon.Infrastructure/Agon.Infrastructure.csproj package \
  Microsoft.EntityFrameworkCore \
  Npgsql.EntityFrameworkCore.PostgreSQL \
  StackExchange.Redis \
  Microsoft.AspNetCore.SignalR.Core \
  Microsoft.Extensions.AI.OpenAI \
  Anthropic.SDK
```

---

## Frontend (Next.js)

### Tech Stack

- **Next.js 16** with App Router
- **React 19**
- **TypeScript 5**
- **Tailwind CSS v4**
- Current scope: scaffold/WIP (`app/layout.tsx`, `app/page.tsx`)

### Getting Started

```bash
cd frontend
npm install
npm run dev
```

Open [http://localhost:3000](http://localhost:3000) to see the app.

### Current Project Structure

```
frontend/
├── app/
│   ├── layout.tsx
│   ├── page.tsx
│   └── globals.css
│
├── public/
├── next.config.ts
└── package.json
```

### Frontend Roadmap (Planned)

- Thread View and Truth Map UI
- Real-time SignalR updates
- Session creation and follow-up UX
- Artifact browsing and export actions

---

## Testing & Quality

### Test Coverage

[![CI](https://github.com/simonholmes001/agon/actions/workflows/ci.yaml/badge.svg)](https://github.com/simonholmes001/agon/actions/workflows/ci.yaml)
[![Update Badges](https://github.com/simonholmes001/agon/actions/workflows/update-badges.yaml/badge.svg)](https://github.com/simonholmes001/agon/actions/workflows/update-badges.yaml)

Coverage and test counts are maintained by CI badges at the top of this README. Badges are automatically refreshed on every push/merge to `main` by `.github/workflows/update-badges.yaml`.

### Testing Strategy

- **Backend**: TDD strict — tests written first, implementation follows
- **Domain**: Pure unit tests (no mocks, blazing fast)
- **Application**: Unit tests with mocked repositories
- **Infrastructure**: Integration tests with in-memory DB and mocked external services
- **Frontend**: UI tests are currently not part of required CI/merge gates for this phase

### Running Tests

Run the full test suite from the repository root:

```bash
# Backend (.NET)
dotnet test backend/Agon.sln --verbosity minimal

# CLI (TypeScript)
npm --prefix cli test
```

Individual project tests:

```bash
# Backend domain tests only
dotnet test backend/tests/Agon.Domain.Tests

# CLI tests with coverage
cd cli && npm run test:coverage
```

---

## Local Pre-Commit Hooks

Local pre-commit test gate is available at `.githooks/pre-commit` and runs:
- CLI tests
- Backend tests
- Changeset validation (if CLI code changed)

To enable local git hooks:

```bash
git config core.hooksPath .githooks
```

Changeset validation is enforced by default in local pre-commit hook when `cli/` changes are staged.

---

## CLI Release Process

### Workflows

- CI checks: `.github/workflows/ci.yaml`
- Release + publish: `.github/workflows/publish-cli.yaml`

### Manual Steps (Developer)

1. In any PR that changes `cli/`, create a changeset:
   ```bash
   npx --yes @changesets/cli add --cwd cli
   # Choose patch, minor, or major
   ```
2. Merge the feature PR to `main`
3. Merge the auto-generated release PR (title: `chore(release): version @agon_agents/cli`)

### Automated Steps (GitHub Actions)

1. PR CI fails if `cli/` changed without a `cli/.changeset/*.md` file
2. After merge to `main`, Changesets action:
   - Reads pending changesets
   - Creates/updates the release PR
   - Bumps `cli/package.json` and updates changelog
3. When release PR is merged, Changesets action publishes to npm (`latest`) using trusted publishing (OIDC)

### Requirements

- npm Trusted Publisher configured for this repository/workflow
- `publish-cli.yaml` has `id-token: write` permission
- Package passes `npm run release:publish` (tests + pack dry-run + publish)

---

## Architecture Documentation

Full architecture, round policy, agent roster, and coding guidelines are in `.github/instructions/`:

- `architecture.instructions.md` — System topology, runtime responsibilities, data model
- `prd-agon-core.instructions.md` — Product requirements, features, UX flows
- `round-policy.instructions.md` — Phase transitions, convergence rules, HITL policy
- `copilot.instructions.md` — Coding rules, TDD requirements, output templates
- `schemas.instructions.md` — JSON schemas for all data structures
- `backend-implementation.instructions.md` — MAF integration, solution structure, layer rules

---

## Current Status (March 2026)

### ✅ Completed

- **Backend API layer**: Session/message/artifact endpoints are implemented in `Agon.Api`.
- **Debate orchestration**: `Orchestrator` + `AgentRunner` are active and tested.
- **CLI shell**: `agon` default interactive shell with slash commands and follow-up flow.
- **Root .gitignore**: Consolidated ignore patterns for monorepo.
- **TDD discipline**: backend + CLI test suites run in local hooks and CI.

### 🚧 In Progress

- **Frontend**: Next.js UI iteration and backend integration.

### 📋 Next Steps

1. **Frontend**:
   - Scaffold core layout (Thread View + Truth Map Drawer)
   - Implement SignalR client connection manager
   - Build session creation flow
   - Implement real-time agent token streaming

2. **Integration**:
   - Connect frontend to backend API
   - End-to-end testing with real LLM providers
   - Performance optimization (streaming latency, patch application)

3. **CLI polish**:
   - Continue shell UX refinements (formatting, streaming indicators, guidance text)
   - Expand command help and parameter controls
   - Harden long-running follow-up/debate visibility behavior

---

## Contributing

This project follows strict **Clean Architecture** and **TDD** principles. See `.github/instructions/copilot.instructions.md` for full coding guidelines.

### Key Rules

1. **TDD is non-negotiable**: Write tests first, then implementation (RED → GREEN → REFACTOR)
2. **Clean Architecture layers**: Domain has zero framework dependencies
3. **No Docker required**: All tests use in-memory databases or mocked clients
4. **Structured logging**: Use `ILogger<T>` (backend) or `lib/logger.ts` (frontend)
5. **Error boundaries**: Every route must have `error.tsx` and root must have `global-error.tsx`

### Reporting Issues and Feature Requests

If you encounter a bug or have a feature request, please [open an issue](https://github.com/simonholmes001/agon/issues) and include:

- A clear, descriptive title
- Steps to reproduce the problem (for bugs)
- Expected and actual behaviour
- Your environment (OS, Node.js version, .NET version, CLI version)
- Any relevant log output (check `~/.agon/logs/agon.log` for CLI logs)

For feature requests, describe the problem you are trying to solve and your proposed solution. The more context you provide, the easier it is for maintainers to evaluate and prioritise.

### Submitting Pull Requests

1. **Fork** the repository and create a feature branch from `main`.
2. Follow the TDD cycle: write a failing test first, then implement the fix or feature.
3. Ensure all existing tests continue to pass (`dotnet test backend/Agon.sln` and `npm --prefix cli test`).
4. If your change touches the CLI package, add a changeset before opening the PR:
   ```bash
   npx --yes @changesets/cli add --cwd cli
   ```
5. Keep pull requests focused — one logical change per PR makes review faster.
6. Fill out the PR description with a summary of what changed and why.
7. PRs that reduce test coverage below the current baseline will not be merged.

---

## License

Proprietary — All rights reserved.

---

## Contact

For questions or contributions, contact the development team.

---

## Notes for Project Maintainers

This section captures operational notes relevant to maintainers of the Agon repository.

### Badge Maintenance

CI badges at the top of this README are auto-updated by `.github/workflows/update-badges.yaml` on every push to `main`. If badge values appear stale, manually trigger the workflow from the Actions tab.

### npm Publish

The CLI package (`@agon_agents/cli`) is published to npm via GitHub Actions using OIDC trusted publishing. No npm token is stored in repository secrets — ensure the npm Trusted Publisher is configured for `publish-cli.yaml` with `id-token: write` permission before any release.

### Dependency Updates

Dependencies should be updated regularly:

```bash
# CLI
cd cli && npm outdated

# Backend
cd backend && dotnet list package --outdated
```

Run the full test suite after any dependency bump before merging.

### Architecture Documentation

All living architecture documents are in `.github/instructions/`. Update these files alongside code changes to keep them accurate. They are auto-injected into Copilot sessions, so stale documentation directly affects AI-assisted development quality.
