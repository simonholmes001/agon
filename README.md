# Agon — Living Strategy Room

[![Next.js](https://img.shields.io/badge/Next.js-16-000000?style=flat-square&logo=next.js)](https://nextjs.org)
[![React](https://img.shields.io/badge/React-19-61DAFB?style=flat-square&logo=react&logoColor=000)](https://react.dev)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?style=flat-square&logo=typescript&logoColor=fff)](https://www.typescriptlang.org)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS-v4-06B6D4?style=flat-square&logo=tailwindcss&logoColor=fff)](https://tailwindcss.com)
[![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://dotnet.microsoft.com)
[![Vitest](https://img.shields.io/badge/Tested_with-Vitest-6E9F18?style=flat-square&logo=vitest&logoColor=fff)](https://vitest.dev)
[![xUnit](https://img.shields.io/badge/Tested_with-xUnit-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://xunit.net)
[![Tests](https://img.shields.io/badge/Tests-719_passing-brightgreen?style=flat-square)]()
[![Coverage](https://img.shields.io/badge/Coverage-40%25_lines-red?style=flat-square)]()
[![TDD](https://img.shields.io/badge/Methodology-TDD-red?style=flat-square)]()
[![Licence](https://img.shields.io/badge/Licence-Private-lightgrey?style=flat-square)]()

> Multi-agent AI analysis that transforms raw ideas into production-ready documentation.

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

The Agon CLI is distributed as an npm package and can be installed globally or used via `npx`.

#### Prerequisites

- **Node.js 20+** ([download](https://nodejs.org/))
- **npm** (included with Node.js)

#### Global Installation (Recommended)

Install globally to use the `agon` command from anywhere:

```bash
npm install -g @agon_agents/cli
```

Update to the newest stable release:

```bash
npm install -g @agon_agents/cli@latest
```

Verify installation:

```bash
agon --version
```

When you run `agon`, the shell now checks npm for `@agon_agents/cli@latest`.  
If a newer stable version exists, Agon prints an in-shell update notice with the install command.

Launch the interactive shell:

```bash
agon
```

Or start a new session directly:

```bash
agon start "Build a SaaS platform for healthcare"
```

#### Using npx (No Installation Required)

Run Agon without installing:

```bash
npx @agon_agents/cli start "Your idea here"
```

Launch interactive shell:

```bash
npx @agon_agents/cli
```

#### Local Installation (Project-Specific)

Install as a development dependency in your project:

```bash
npm install --save-dev @agon_agents/cli
```

Then use via npm scripts in `package.json`:

```json
{
  "scripts": {
    "agon": "agon"
  }
}
```

Run with:

```bash
npm run agon
```

#### Uninstalling

To remove the global installation:

```bash
npm uninstall -g @agon_agents/cli
```

To remove from a project:

```bash
npm uninstall @agon_agents/cli
```

#### Quick Start

After installation, start your first session:

```bash
# Launch interactive shell
agon

# Or start directly with an idea
agon start "Migrate our monolith to microservices"

# Check session status
agon status

# View generated artifacts
agon show verdict
agon show plan
agon show prd
```

For full command documentation, run:

```bash
agon --help
```

### Web Application (In Development)

Browser-based interface with visual Truth Map explorer and real-time agent streaming. Coming soon.

### iOS Application (In Development)

Native iOS app for on-the-go strategy analysis. Coming soon.

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
├── frontend/             # Next.js App Router frontend (scaffold/WIP)
│
└── .github/
    ├── workflows/        # CI + badge automation
    ├── scripts/          # Badge update script
    └── instructions/     # Architecture & coding rules
```

---

## Getting Started

### Prerequisites

- **.NET 9.0 SDK** ([download](https://dotnet.microsoft.com/download))
- **Node.js 20+** ([download](https://nodejs.org/))
- **PostgreSQL 16+** (for production; tests use in-memory DB)
- **Redis 7+** (for production; tests use mocked client)

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

## Development Workflow

### 1. Backend Development

```bash
cd backend

# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~PatchValidatorTests"

# Build solution
dotnet build

# Watch mode (auto-rebuild on changes)
dotnet watch --project src/Agon.Application
```

### 2. Frontend Development

```bash
cd frontend

# Development server with hot reload
npm run dev

# Linting
npm run lint

# Build for production
npm run build
```

### 3. Testing Strategy

- **Backend**: TDD strict — tests written first, implementation follows
- **Domain**: Pure unit tests (no mocks, blazing fast)
- **Application**: Unit tests with mocked repositories
- **Infrastructure**: Integration tests with in-memory DB and mocked external services
- **Frontend**: UI tests are currently not part of required CI/merge gates for this phase

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

---

## License

Proprietary — All rights reserved.

---

## Contact

For questions or contributions, contact the development team.

---

## Addendum (March 2026): Backend + CLI Functionality

[![CI](https://github.com/simonholmes001/agon/actions/workflows/ci.yaml/badge.svg)](https://github.com/simonholmes001/agon/actions/workflows/ci.yaml)
[![Update Badges](https://github.com/simonholmes001/agon/actions/workflows/update-badges.yaml/badge.svg)](https://github.com/simonholmes001/agon/actions/workflows/update-badges.yaml)

This addendum documents the currently implemented runtime capabilities and how README test/coverage badges are maintained.

### Backend (Agon.Api) — Implemented Capabilities

- Session lifecycle endpoints are implemented in `backend/src/Agon.Api/Controllers/SessionsController.cs`:
  - `POST /sessions`
  - `GET /sessions/{id}`
  - `POST /sessions/{id}/start`
  - `POST /sessions/{id}/messages`
  - `GET /sessions/{id}/messages`
  - `GET /sessions/{id}/truthmap`
  - `GET /sessions/{id}/snapshots`
  - `GET /sessions/{id}/artifacts/{type}`
  - `GET /sessions/{id}/artifacts`
- Debate orchestration is wired through `Orchestrator` and `AgentRunner` in `backend/src/Agon.Application/Orchestration/`.
- Real-time signaling is enabled through SignalR (`/hubs/debate`) in `backend/src/Agon.Api/Program.cs`.
- Artifact synthesis currently includes verdict/plan plus derived risks/assumptions in controller output.

### CLI — Implemented Capabilities

- Command-driven usage:
  - `agon start "<idea>"`
  - `agon follow-up "<message>"` (alias of `answer`)
  - `agon status`
  - `agon show <artifact>`
  - `agon sessions`
  - `agon resume <session-id>`
  - `agon config`
- Default interactive shell:
  - Running `agon` (no subcommand) launches a codex-style interactive shell.
  - Supported slash commands:
    - `/help`
    - `/params`
    - `/set <apiUrl|defaultFriction|researchEnabled|logLevel> <value>`
    - `/new`
    - `/session <session-id>`
    - `/status [session-id]`
    - `/show <artifact> [--refresh] [--raw]`
    - `/follow-up <message>`
    - `/exit` (alias: `/quit`, `/eot`)

### CLI Installation (npm package)

- Canonical documentation source: this root `README.md` (no separate tracked CLI README).
- Runtime requirements for CLI users:
  - Node.js 20+
  - Reachable Agon backend API URL

- Global install from npm:

```bash
npm install -g @agon_agents/cli
```

- Update to newest stable:

```bash
npm install -g @agon_agents/cli@latest
```

- Shell startup performs a lightweight npm check and prints an update notice when a newer `latest` version is available.

- Configure backend API URL:

```bash
agon config set apiUrl https://api.your-domain.com
```

- Launch interactive shell:

```bash
agon
```

- Non-interactive start:

```bash
agon start "I need a PRD for my project"
```

### CLI Release Workflow (Manual vs Automated)

**Workflows involved**
- CI checks: `.github/workflows/ci.yaml`
- Release + publish: `.github/workflows/publish-cli.yaml`

**Manual steps (developer)**
1. In any PR that changes `cli/`, create a changeset:
   - `cd cli`
   - `npx changeset`
   - Choose `patch`, `minor`, or `major`
2. Merge the feature PR to `main`.
3. Merge the auto-generated release PR:
   - Title pattern: `chore(release): version @agon_agents/cli`

**Automated steps (GitHub Actions)**
1. PR CI fails if `cli/` changed but no `cli/.changeset/*.md` file exists.
2. After merge to `main`, Changesets action:
   - reads pending changesets
   - creates/updates the release PR
   - bumps `cli/package.json` and updates changelog in that PR
3. When release PR is merged, Changesets action publishes to npm (`latest`) using trusted publishing (OIDC).

**What is automated vs not automated**
- Automated:
  - validation that a changeset exists
  - version number write/update in release PR
  - changelog generation in release PR
  - npm publish after release PR merge
- Manual:
  - selecting bump type (`patch`/`minor`/`major`)
  - merging the release PR

**Requirements**
- npm Trusted Publisher configured for this repository/workflow
- `publish-cli.yaml` has `id-token: write` (already set)
- package passes `npm run release:publish` (tests + pack dry-run + publish)

### npm Release and Version Model

- npm package versions are SemVer (for example `1.4.2`).
- npm does not auto-increment your package version.
- Developers select bump level in a changeset (`patch`, `minor`, `major`); release automation writes the final `cli/package.json` version.
- A published `<package>@<version>` is immutable on npm; you cannot republish the same version with different contents.
- Dist-tags control install channels:
  - `latest` is the default stable tag used by `npm install -g @agon_agents/cli`

### Test + Coverage Badges (Auto-Updated on `main`)

- README badges are automatically refreshed on every push/merge to `main` by:
  - Workflow: `.github/workflows/update-badges.yaml`
  - Script: `.github/scripts/update-readme-badges.sh`
- The workflow:
  1. Runs CLI unit tests
  2. Runs backend tests (`dotnet test`)
  3. Computes total passing test count (CLI + backend)
  4. Computes combined line coverage using:
     - CLI V8 coverage summary (`cli/coverage/coverage-summary.json`)
     - backend Cobertura attachments (`coverage.cobertura.xml`)
  5. Updates the `Tests` badge and `Coverage` badge in `README.md`
  6. Commits and pushes badge changes back to `main` (if changed)
- Coverage percentage shown in the README badge is the combined line coverage across CLI + backend test runs.

### Local Hooks and Quality Gates

- Local pre-commit test gate is available at `.githooks/pre-commit` and runs:
  - CLI tests
  - backend tests
- If staged changes include `cli/` without a staged `cli/.changeset/*.md` file, the hook prints a release reminder:
  - `cd cli && npx changeset`
  - `git add cli/.changeset/*.md`
- Optional strict mode for local commits:
  - `AGON_ENFORCE_CHANGESET=1 git commit ...`
  - In strict mode, commit is blocked when CLI changes are staged without a changeset file.
- To enable local git hooks for this repo:

```bash
git config core.hooksPath .githooks
```
