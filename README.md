# Agon — Living Strategy Room

[![Next.js](https://img.shields.io/badge/Next.js-16-000000?style=flat-square&logo=next.js)](https://nextjs.org)
[![React](https://img.shields.io/badge/React-19-61DAFB?style=flat-square&logo=react&logoColor=000)](https://react.dev)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?style=flat-square&logo=typescript&logoColor=fff)](https://www.typescriptlang.org)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS-v4-06B6D4?style=flat-square&logo=tailwindcss&logoColor=fff)](https://tailwindcss.com)
[![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://dotnet.microsoft.com)
[![Vitest](https://img.shields.io/badge/Tested_with-Vitest-6E9F18?style=flat-square&logo=vitest&logoColor=fff)](https://vitest.dev)
[![xUnit](https://img.shields.io/badge/Tested_with-xUnit-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://xunit.net)
[![Tests](https://img.shields.io/badge/Tests-395_passing-brightgreen?style=flat-square)]()
[![Coverage](https://img.shields.io/badge/Coverage-83%25_lines-green?style=flat-square)]()
[![TDD](https://img.shields.io/badge/Methodology-TDD-red?style=flat-square)]()
[![Licence](https://img.shields.io/badge/Licence-Private-lightgrey?style=flat-square)]()

> A council of specialist AI agents debates your idea so you don't ship your blind spots.

Agon is an agentic idea-analysis workspace. You bring a raw idea — a product concept, a technical proposal, a strategic pivot — and a council of AI agents drawn from different model providers tears it apart, stress-tests it, and reassembles it into a decision-grade output pack.

Unlike a single-prompt AI chat, Agon maintains a **living Truth Map**: a structured, versioned graph of claims, assumptions, risks, and decisions that every agent reads from and writes to. If a constraint changes mid-session, the system propagates that change and agents re-evaluate automatically.

---

## Project Overview

Agon is an **agentic idea analysis workspace** — a "living strategy room" where a user brings an idea and a council of specialist AI agents debates, challenges, and develops it into a decision-grade output pack.

Unlike a linear "input → debate → output" pipeline, Agon maintains a continuously updated **Global Workspace** ("Truth Map") that all agents read from and write to. If a constraint changes mid-session, the system updates the state and agents immediately re-evaluate their prior claims.

---

## Repository Structure

```
Agon/
├── backend/              # .NET 9 backend
│   ├── src/
│   │   ├── Agon.Domain/              # ✅ Pure business logic (66 tests)
│   │   ├── Agon.Application/         # ✅ Orchestration & use-cases (56 tests)
│   │   └── Agon.Infrastructure/      # ✅ Persistence, MAF, SignalR (42 tests)
│   ├── tests/
│   │   ├── Agon.Domain.Tests/
│   │   ├── Agon.Application.Tests/
│   │   └── Agon.Infrastructure.Tests/
│   └── Agon.sln
│
├── frontend/             # Next.js App Router frontend
│   ├── app/              # App Router pages
│   ├── components/       # React components (to be created)
│   └── lib/              # Utilities, logger, API client (to be created)
│
└── .github/
    └── instructions/     # Architecture & coding rules

Total Tests: 164 passing (164/164)
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
│         API Layer (future)          │  ← HTTP endpoints, middleware
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

**Current status:** ✅ 164/164 tests passing

### Test Coverage

| Layer | Tests | Status |
|---|---|---|
| Domain | 66 | ✅ Complete |
| Application | 56 | ✅ Complete |
| Infrastructure | 42 | ✅ Complete |
| - AgentResponseParser | 8 | ✅ |
| - MafCouncilAgent | 3 | ✅ |
| - TruthMapRepository | 8 | ✅ |
| - SessionRepository | 9 | ✅ |
| - RedisSnapshotStore | 7 | ✅ |
| - SignalREventBroadcaster | 8 | ✅ |

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

- **Next.js 15** with App Router
- **TypeScript**
- **Tailwind CSS**
- **shadcn/ui** components
- **Framer Motion** for animations
- **SignalR client** for real-time updates

### Getting Started

```bash
cd frontend
npm install
npm run dev
```

Open [http://localhost:3000](http://localhost:3000) to see the app.

### Project Structure (to be built)

```
frontend/
├── app/
│   ├── layout.tsx          # Root layout with SignalR provider
│   ├── page.tsx            # Landing page
│   ├── session/
│   │   ├── new/            # Session creation flow
│   │   └── [id]/           # Active session view (Thread + Map)
│   └── globals.css
│
├── components/
│   ├── ui/                 # shadcn/ui primitives
│   ├── thread/             # Thread view components
│   ├── truth-map/          # Truth Map drawer/panel
│   └── session/            # Session controls (friction slider, etc.)
│
├── lib/
│   ├── api/                # REST API client
│   ├── signalr/            # SignalR connection manager
│   ├── logger.ts           # Structured logging
│   └── utils.ts
│
└── types/                  # TypeScript definitions
```

### Key Features

- **Thread View**: Premium group-chat aesthetic with agent cards
- **Truth Map Drawer**: Bottom sheet (mobile) / right panel (desktop)
- **Friction Slider**: 0-100 control affecting tone and convergence thresholds
- **Real-time Updates**: SignalR streams tokens, patches, confidence changes
- **HITL Controls**: "Tap an Agent" for challenges, deep dives, constraint changes

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

# Type checking
npm run type-check

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
- **Frontend**: (to be added) Component tests with React Testing Library

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

- **Domain Layer**: All business logic, engines, validators (66 tests passing)
- **Application Layer**: Orchestrator, AgentRunner, SessionService (56 tests passing)
- **Infrastructure Layer**: PostgreSQL, Redis, SignalR, MAF integration (42 tests passing)
- **Root .gitignore**: Consolidated ignore patterns for monorepo
- **TDD Discipline**: 164/164 tests passing, all following RED-GREEN-REFACTOR

### 🚧 In Progress

- **API Layer**: ASP.NET Core Web API with REST endpoints (not started)
- **Frontend**: Next.js UI with SignalR client (not started)

### 📋 Next Steps

1. **API Layer** (backend):
   - Create `Agon.Api` project
   - Implement REST endpoints (`POST /sessions`, `GET /sessions/{id}/truthmap`, etc.)
   - Add SignalR hub registration
   - Configure DI container with all repositories and services

2. **Frontend**:
   - Scaffold core layout (Thread View + Truth Map Drawer)
   - Implement SignalR client connection manager
   - Build session creation flow
   - Implement real-time agent token streaming

3. **Integration**:
   - Connect frontend to backend API
   - End-to-end testing with real LLM providers
   - Performance optimization (streaming latency, patch application)

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
