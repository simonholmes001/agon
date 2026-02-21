# Agon — Living Strategy Room

[![Next.js](https://img.shields.io/badge/Next.js-16-000000?style=flat-square&logo=next.js)](https://nextjs.org)
[![React](https://img.shields.io/badge/React-19-61DAFB?style=flat-square&logo=react&logoColor=000)](https://react.dev)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?style=flat-square&logo=typescript&logoColor=fff)](https://www.typescriptlang.org)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS-v4-06B6D4?style=flat-square&logo=tailwindcss&logoColor=fff)](https://tailwindcss.com)
[![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://dotnet.microsoft.com)
[![Vitest](https://img.shields.io/badge/Tested_with-Vitest-6E9F18?style=flat-square&logo=vitest&logoColor=fff)](https://vitest.dev)
[![xUnit](https://img.shields.io/badge/Tested_with-xUnit-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://xunit.net)
[![Tests](https://img.shields.io/badge/Tests-312_passing-brightgreen?style=flat-square)]()
[![Coverage](https://img.shields.io/badge/Coverage-87%25_lines-green?style=flat-square)]()
[![TDD](https://img.shields.io/badge/Methodology-TDD-red?style=flat-square)]()
[![Licence](https://img.shields.io/badge/Licence-Private-lightgrey?style=flat-square)]()

> A council of specialist AI agents debates your idea so you don't ship your blind spots.

Agon is an agentic idea-analysis workspace. You bring a raw idea — a product concept, a technical proposal, a strategic pivot — and a council of AI agents drawn from different model providers tears it apart, stress-tests it, and reassembles it into a decision-grade output pack.

Unlike a single-prompt AI chat, Agon maintains a **living Truth Map**: a structured, versioned graph of claims, assumptions, risks, and decisions that every agent reads from and writes to. If a constraint changes mid-session, the system propagates that change and agents re-evaluate automatically.

---

## Key Concepts

| Concept | Description |
|---|---|
| **Truth Map** | Shared state graph (claims, assumptions, risks, decisions, evidence) — the single source of truth for the session. Agents propose patches; the Orchestrator validates and applies them. |
| **Council** | Seven specialist agents: Socratic Clarifier, Framing Challenger, Product Strategist, Technical Architect, Contrarian / Red Team, Research Librarian, Synthesis + Validation. |
| **Multi-Model** | Each agent uses a different LLM provider (GPT-5.2, Gemini 3, Claude Opus 4.6, DeepSeek-V3.2) — diversity of reasoning reduces blind spots. |
| **Friction Slider** | User-controlled dial (0–100) that modulates agent tone *and* convergence thresholds — from brainstorm (low friction) to adversarial red-team (high friction). |
| **Bounded Debate** | Hard-capped rounds and token budgets. Sessions always terminate. Degradation is graceful and surfaced, never silent. |
| **HITL** | Human-in-the-loop: pause, challenge a claim, force a deep dive, change a constraint mid-session — with full change propagation. |
| **Pause-and-Replay** | Fork a session from any prior snapshot, change a constraint, and re-run the debate to compare scenarios. |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Frontend — Next.js 16 (App Router) + Tailwind v4 + shadcn/ui  │
│  Real-time streaming via SignalR                                │
└──────────────────────────┬──────────────────────────────────────┘
                           │ HTTPS + WebSockets
┌──────────────────────────▼──────────────────────────────────────┐
│  Agon.Api — ASP.NET Core host (thin — routing + DI only)       │
├─────────────────────────────────────────────────────────────────┤
│  Agon.Application — Orchestrator (deterministic state machine)  │
│  AgentRunner · SessionService · SnapshotService                 │
│  ICouncilAgent · ITruthMapRepository · IEventBroadcaster        │
├─────────────────────────────────────────────────────────────────┤
│  Agon.Domain — Pure domain, ZERO framework dependencies         │
│  TruthMap · PatchValidator · RoundPolicy · ConvergenceEvaluator │
│  ConfidenceDecayEngine · ChangeImpactCalculator                 │
│  AgentSystemPrompts · Entity types (Claims, Risks, etc.)        │
├─────────────────────────────────────────────────────────────────┤
│  Agon.Infrastructure — MAF agents · DB · SignalR · Blob         │
│  MafCouncilAgent (wraps ChatClientAgent via IChatClient)        │
│  FakeCouncilAgent (canned responses for tests)                  │
└──────┬───────────┬───────────┬───────────┬──────────────────────┘
       │           │           │           │
   PostgreSQL   pgvector     Redis    Blob Storage
   (sessions,   (semantic   (round    (exports,
    Truth Map,   memory)     state,    snapshots)
    artifacts)               locks)
```

### Clean Architecture Layers

Dependencies point **inward** — Domain has zero framework dependencies, Application defines interfaces, Infrastructure implements them.

```
Agon.Api → Agon.Application, Agon.Infrastructure
Agon.Infrastructure → Agon.Application
Agon.Application → Agon.Domain
Agon.Domain → (nothing)
```

### MAF Integration Strategy

[Microsoft Agent Framework](https://github.com/microsoft/agent-framework) (`1.0.0-rc1`) is used for the **agent call layer only** — wrapping LLM providers behind `IChatClient` from `Microsoft.Extensions.AI`. MAF's workflow engine (`AgentWorkflowBuilder`, `GroupChatManager`) is **not used** for orchestration because:

1. **Patch-based communication** — Agon agents communicate via structured `TruthMapPatch` operations to a shared Truth Map, not MAF's `ChatMessage` lists.
2. **Conditional phase transitions** — Our session state machine (INTAKE → CLARIFICATION → DEBATE → SYNTHESIS → DELIVER) has conditional branches, HITL interrupts, and micro-rounds that don't map to `BuildSequential`/`BuildConcurrent`.
3. **Deterministic orchestration** — MAF's `GroupChatManager` uses an LLM to route between agents, violating our hard rule that LLM outputs cannot trigger state transitions.
4. **Post-response processing** — After each agent call, the Orchestrator must validate patches, run the Confidence Decay Engine, update convergence scores, and check budget — none of which are MAF primitives.

The custom `Orchestrator` in the Application layer handles all session policy. MAF handles the "how" (making LLM calls); our Orchestrator handles the "what and when" (state machine logic).

Full architecture specification: [`.github/instructions/architecture.instructions.md`](.github/instructions/architecture.instructions.md)
Full implementation guide: [`.github/instructions/backend-implementation.instructions.md`](.github/instructions/backend-implementation.instructions.md)

---

## Tech Stack

### Frontend

| Technology | Purpose |
|---|---|
| [Next.js 16](https://nextjs.org) (App Router) | React framework, server components, file-system routing |
| [React 19](https://react.dev) | UI library |
| [Tailwind CSS v4](https://tailwindcss.com) | Utility-first styling |
| [shadcn/ui](https://ui.shadcn.com) | Accessible component primitives (Radix UI) |
| [Framer Motion](https://www.framer.com/motion/) | Animations |
| [Lucide React](https://lucide.dev) | Icons |
| [Vitest](https://vitest.dev) + [Testing Library](https://testing-library.com) | Unit and component tests |

### Backend

| Technology | Purpose |
|---|---|
| [ASP.NET Core (.NET 9)](https://dotnet.microsoft.com) | API host and composition root |
| [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) `1.0.0-rc1` | Agent call layer — `ChatClientAgent`, `IChatClient`, provider adapters |
| [`IChatClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient) (`Microsoft.Extensions.AI`) | Provider-agnostic LLM interface (replaces spec's `IChatModelClient`) |
| `Microsoft.Agents.AI.OpenAI` | OpenAI adapter (GPT-5.2 Thinking) |
| `Microsoft.Agents.AI.Anthropic` | Anthropic adapter (Claude Opus 4.6) |
| [PostgreSQL](https://www.postgresql.org) + [pgvector](https://github.com/pgvector/pgvector) | Sessions, Truth Map (JSONB + normalised entities), semantic memory |
| [Redis](https://redis.io) | Ephemeral round state, locks, rate limits |
| [SignalR](https://learn.microsoft.com/aspnet/core/signalr) | Real-time streaming (tokens, patches, convergence) |
| [xUnit](https://xunit.net) + [FluentAssertions](https://fluentassertions.com) | Testing framework |

### Model Providers

| Agent | Primary Model | Provider |
|---|---|---|
| Socratic Clarifier | GPT-5.2 Thinking | OpenAI |
| Framing Challenger | Gemini 3 (thinking: high) | Google |
| Product Strategist | Claude Opus 4.6 | Anthropic |
| Technical Architect | DeepSeek-V3.2 (thinking) | DeepSeek |
| Contrarian / Red Team | Gemini 3 (thinking: high) | Google |
| Research Librarian | GPT-5.2 Thinking | OpenAI |
| Synthesis + Validation | GPT-5.2 Thinking | OpenAI |

All providers are accessed via `IChatClient` — interchangeable behind the interface. DeepSeek uses OpenAI-compatible API via `OpenAIClient` with a custom endpoint.

---

## Project Structure

```
agon/
├── .github/
│   └── instructions/               # System specs and coding rules
│       ├── architecture.instructions.md
│       ├── backend-implementation.instructions.md
│       ├── backlog.instructions.md
│       ├── copilot.instructions.md
│       ├── prd-agon-core.instructions.md
│       ├── prompt-engineering-config.instructions.md
│       ├── round-policy.instructions.md
│       └── schemas.instructions.md
├── backend/                         # .NET Clean Architecture
│   ├── Agon.sln
│   ├── src/
│   │   ├── Agon.Domain/            # Pure domain — ZERO framework deps
│   │   │   ├── Agents/             # AgentId, AgentSystemPrompts
│   │   │   ├── TruthMap/           # TruthMap, PatchValidator, entities
│   │   │   ├── Sessions/           # SessionPhase, RoundPolicy, ConvergenceEvaluator
│   │   │   ├── Engines/            # ConfidenceDecayEngine, ChangeImpactCalculator
│   │   │   └── Snapshots/          # SessionSnapshot, ForkRequest
│   │   ├── Agon.Application/       # Orchestration use-cases + interfaces
│   │   │   ├── Orchestration/      # Orchestrator (deterministic transitions), AgentRunner
│   │   │   ├── Interfaces/         # ICouncilAgent, ITruthMapRepository, ISessionRepository
│   │   │   └── Services/           # SessionService, SnapshotService
│   │   ├── Agon.Infrastructure/    # In-memory adapters + SignalR broadcaster
│   │   │   ├── Agents/             # FakeCouncilAgent
│   │   │   ├── Persistence/        # InMemorySessionRepository, InMemoryTruthMapRepository
│   │   │   └── SignalR/            # DebateHub + SignalREventBroadcaster
│   │   └── Agon.Api/               # Thin host — routing + DI
│   │       └── Program.cs          # Core session endpoints
│   └── tests/
│       ├── Agon.Domain.Tests/       # Unit tests (TDD)
│       ├── Agon.Application.Tests/  # Orchestration + service tests
│       ├── Agon.Infrastructure.Tests/ # In-memory adapter tests
│       └── Agon.Api.Tests/          # API endpoint integration tests
├── frontend/                        # Next.js application
│   ├── app/
│   │   ├── layout.tsx          # Root layout (dark mode, Geist fonts)
│   │   ├── page.tsx            # Landing page
│   │   ├── session/
│   │   │   ├── new/page.tsx    # New session creation
│   │   │   └── [id]/page.tsx   # Live debate session view
│   │   └── sessions/page.tsx   # Past sessions list
│   ├── components/
│   │   ├── landing/
│   │   │   └── hero-section.tsx
│   │   ├── session/
│   │   │   ├── agent-message-card.tsx
│   │   │   ├── message-composer.tsx
│   │   │   ├── session-header.tsx
│   │   │   ├── thread-view.tsx
│   │   │   └── truth-map-drawer.tsx
│   │   └── ui/                 # shadcn/ui primitives
│   ├── lib/
│   │   ├── constants.ts        # Agent registry, phase labels, friction config
│   │   ├── realtime/           # Debate hub client + reconnect/resync wiring
│   │   ├── utils.ts            # Utilities (cn)
│   │   └── test-utils.tsx      # Custom test render wrapper
│   └── types/
│       └── session.ts          # TypeScript types mirroring backend schemas
└── README.md
```

---

## Getting Started

### Prerequisites

- **Node.js** ≥ 20
- **npm** ≥ 10
- **.NET SDK** ≥ 9.0

### Install and Run

```bash
# Clone the repository
git clone https://github.com/simonholmes001/agon.git
cd agon

# Enable pre-commit hooks
git config core.hooksPath .githooks

# --- Frontend ---
cd frontend
npm install
npm install @microsoft/signalr   # required for live SignalR streaming
npm run dev
# Open http://localhost:3000

# --- Backend ---
cd ../backend
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Agon.Api
```

For local frontend-to-backend realtime transport:

```bash
# frontend/.env.local
BACKEND_API_BASE_URL=http://localhost:5000
# Optional override (defaults to BACKEND_API_BASE_URL or http://localhost:5000)
# NEXT_PUBLIC_DEBATE_HUB_URL=http://localhost:5000/hubs/debate

# backend/.env (or exported shell environment)
# Provider keys (missing keys now surface as explicit system error transcript messages;
# no fake-agent fallback is used)
# OPENAI_KEY=your-openai-api-key
# GEMINI_KEY=your-gemini-api-key
# ANTHROPIC_KEY=your-anthropic-api-key
# CLAUDE_KEY=your-anthropic-api-key   # supported alias
# DEEPSEEK_KEY=your-deepseek-api-key
#
# Optional model overrides
# OPENAI_MODEL=gpt-4o-mini
# GEMINI_MODEL=gemini-2.0-flash
# ANTHROPIC_MODEL=claude-3-5-sonnet-latest
# DEEPSEEK_MODEL=deepseek-chat
```

`BACKEND_API_BASE_URL` is used by Next.js route handlers under
`/api/backend/*` to proxy REST calls to ASP.NET Core and avoid CORS and route
collisions with frontend pages.

If `NEXT_PUBLIC_DEBATE_HUB_URL` is not set, the frontend builds the hub URL
from `NEXT_PUBLIC_BACKEND_BASE_URL` (fallback: `BACKEND_API_BASE_URL`,
fallback: `http://localhost:5000`) with `/hubs/debate` appended. This avoids
WebSocket upgrade issues through Next.js route handlers.

The API now enables CORS for local frontend origins by default:
`http://localhost:3000` and `https://localhost:3000`. Override via
`Cors:AllowedOrigins` in backend configuration when needed.

### Available Scripts

**Frontend** — from the `frontend/` directory:

| Command | Description |
|---|---|
| `npm run dev` | Start dev server with Turbopack |
| `npm run build` | Production build |
| `npm run start` | Start production server |
| `npm run lint` | Run ESLint |
| `npm run test` | Run all tests (Vitest) |
| `npm run test:watch` | Run tests in watch mode |

**Backend** — from the `backend/` directory:

| Command | Description |
|---|---|
| `dotnet build` | Build all projects |
| `dotnet test` | Run all tests (xUnit) |
| `dotnet run --project src/Agon.Api` | Start API server |

---

## Testing

All code follows strict **Test-Driven Development (TDD)**: Red → Green → Refactor. No production code without a failing test.

### Frontend

Tests use [Vitest](https://vitest.dev) with [React Testing Library](https://testing-library.com/docs/react-testing-library/intro) and [jsdom](https://github.com/jsdom/jsdom).

```bash
cd frontend
npm run test            # Run all tests
npm run test:watch      # Watch mode
npx vitest run --coverage  # Coverage report
```

### Backend

Tests use [xUnit](https://xunit.net) with [FluentAssertions](https://fluentassertions.com). Domain tests reference only `Agon.Domain` — no mocking frameworks needed. Application tests use [NSubstitute](https://nsubstitute.github.io/) for interface mocking.

```bash
cd backend
dotnet test                                      # Run all tests
dotnet test --collect:"XPlat Code Coverage"      # With coverage
```

**Required test coverage** (non-negotiable):
- `PatchValidator` — schema validation, conflict detection, cross-agent text modification prevention
- `RoundPolicy` — loop termination, budget exhaustion, early convergence
- `ConvergenceEvaluator` — rubric scoring, friction-adjusted thresholds
- `ConfidenceDecayEngine` — decay on challenge, boost on evidence, clamping, threshold flagging
- `ChangeImpactCalculator` — `derived_from` graph traversal, impact set correctness

**Current coverage** — the Tests and Coverage badges at the top of this README are updated automatically by CI on every push.

---

## Session Flow

A session passes through these phases (enforced by the deterministic Orchestrator state machine):

```
INTAKE → CLARIFICATION → DEBATE_ROUND_1 (parallel) → DEBATE_ROUND_2 (crossfire)
  → SYNTHESIS + VALIDATION → [converged?] → DELIVER → POST_DELIVERY
                            → [gaps?] → TARGETED_LOOP (max 2) → re-enter SYNTHESIS
```

- **Clarification** — Socratic Clarifier extracts intent, constraints, success metrics (max 2 rounds, 3 questions each)
- **Round 1 (Divergence)** — All agents analyse in parallel; patches validated and applied in deterministic order
- **Round 2 (Crossfire)** — Agents must explicitly critique each other's claims by entity ID
- **Synthesis** — Single agent synthesises + scores via convergence rubric in one pass
- **Targeted Loop** — If gaps remain, only the responsible agents are dispatched (bounded)
- **Post-Delivery** — Session remains live for questions, challenges, and constraint changes

Full specification: [`.github/instructions/round-policy.instructions.md`](.github/instructions/round-policy.instructions.md)

---

## API Surface

### REST (HTTPS) — Commands and Queries

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/sessions` | Create a new session |
| `GET` | `/sessions/{id}` | Get session state |
| `POST` | `/sessions/{id}/start` | Transition from clarification to debate round 1 and run a council round |
| `GET` | `/sessions/{id}/transcript` | Get persisted transcript provenance for the session |
| `POST` | `/sessions/{id}/messages` | User message (clarification response or post-delivery question) |
| `POST` | `/sessions/{id}/hitl/challenge` | Challenge a specific claim |
| `POST` | `/sessions/{id}/hitl/constraint` | Add/modify constraint (triggers change propagation) |
| `POST` | `/sessions/{id}/hitl/deepdive` | Force deep dive on a claim |
| `POST` | `/sessions/{id}/fork` | Create forked session from snapshot |
| `GET` | `/sessions/{id}/truthmap` | Get current Truth Map state |
| `GET` | `/sessions/{id}/artifacts/{type}` | Retrieve a generated artifact |
| `GET` | `/sessions/{id}/snapshots` | List available round snapshots |

### SignalR (WebSockets) — Real-Time Streaming

The frontend connects to `/hubs/debate` for server-pushed updates. Current baseline events:

`RoundProgress` · `TruthMapPatch`

Planned event expansion (next increments):

`AgentTokens` · `ConfidenceTransition` · `ConvergenceUpdate` · `PendingRevalidation` · `ArtifactReady` · `BudgetWarning`

---

## Development Methodology

This project follows strict **Test-Driven Development (TDD)**:

1. **Red** — Write a failing test that describes the desired behaviour.
2. **Green** — Write the minimum code to make the test pass.
3. **Refactor** — Clean up while keeping tests green.

No production code without a failing test that motivated it.

See [`.github/instructions/copilot.instructions.md`](.github/instructions/copilot.instructions.md) for the full coding guidelines and [`.github/instructions/backend-implementation.instructions.md`](.github/instructions/backend-implementation.instructions.md) for backend implementation decisions.

---

## Output Artifacts

A completed Agon session produces:

| Artifact | Description |
|---|---|
| `Verdict.md` | Go / No-Go recommendation with rationale and contested claims |
| `Plan.md` | Phased plan — MVP / v1 / v2, 30-60-90 day breakdown |
| `PRD.md` | The idea formalised as a complete product requirements document |
| `Risk-Registry.md` | All risks with severity, likelihood, mitigation, source agent |
| `Assumption-Validation.md` | Every assumption with validation steps and confidence |
| `Architecture.mmd` | Mermaid diagram of proposed system architecture |
| `.github/copilot-instructions.md` | Repo-specific development rules for the idea |
| `Scenario-Diff.md` | Decision diff between original and forked scenarios |

---

## Roadmap

### Phase 1 (current)
- [x] Frontend shell — landing, session creation, debate view, sessions list
- [x] Type system mirroring backend schemas
- [x] Agent registry with model assignments and visual identity
- [x] Component test suite (159 tests, 20 files)
- [x] CI pipeline with automated badge updates
- [x] Backend architecture decisions documented (MAF integration strategy)
- [x] Domain model — TruthMap, PatchValidator, RoundPolicy, ConfidenceDecayEngine, ChangeImpactCalculator (TDD)
- [x] Backend vertical slice — Application/Infrastructure/API scaffold with in-memory adapters and core session endpoints
- [ ] Application layer — full Orchestrator state machine, AgentRunner, ICouncilAgent expansion
- [ ] Infrastructure layer — MAF agents, PostgreSQL, Redis, full SignalR event surface (replace in-memory adapters)
- [ ] API layer — full REST endpoints + global exception middleware
- [ ] Frontend–backend integration — replace remaining demo thread/truth-map state with live REST + SignalR event data

### Phase 1.5
- [ ] Map View (desktop graph visualisation)
- [ ] Session Timeline Scrubber for Pause-and-Replay

### Phase 2
- [ ] SwiftUI iOS app
- [ ] Team workspaces and collaborative HITL
- [ ] Attachments + RAG over user documents
- [ ] Simulation mode
- [ ] Continuous monitoring mode

---

## Licence

Private. All rights reserved.
