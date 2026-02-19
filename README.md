# Agon — Living Strategy Room

[![Next.js](https://img.shields.io/badge/Next.js-16-000000?style=flat-square&logo=next.js)](https://nextjs.org)
[![React](https://img.shields.io/badge/React-19-61DAFB?style=flat-square&logo=react&logoColor=000)](https://react.dev)
[![TypeScript](https://img.shields.io/badge/TypeScript-5-3178C6?style=flat-square&logo=typescript&logoColor=fff)](https://www.typescriptlang.org)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS-v4-06B6D4?style=flat-square&logo=tailwindcss&logoColor=fff)](https://tailwindcss.com)
[![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square&logo=dotnet&logoColor=fff)](https://dotnet.microsoft.com)
[![Vitest](https://img.shields.io/badge/Tested_with-Vitest-6E9F18?style=flat-square&logo=vitest&logoColor=fff)](https://vitest.dev)
[![Tests](https://img.shields.io/badge/Tests-63_passing-brightgreen?style=flat-square)]()
[![Coverage](https://img.shields.io/badge/Coverage-79%25_lines-yellow?style=flat-square)]()
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
┌─────────────────────────────────────────────────────────────┐
│  Frontend — Next.js (App Router) + Tailwind + shadcn/ui     │
│  Real-time streaming via SignalR                            │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTPS + WebSockets
┌──────────────────────────▼──────────────────────────────────┐
│  Backend — ASP.NET Core (.NET)                              │
│  Microsoft Agent Framework orchestration                    │
│  Deterministic state machine (Orchestrator)                 │
│  Confidence Decay Engine · Change Impact Calculator         │
└──────┬───────────┬───────────┬───────────┬──────────────────┘
       │           │           │           │
   PostgreSQL   pgvector     Redis    Blob Storage
   (sessions,   (semantic   (round    (exports,
    Truth Map,   memory)     state,    snapshots)
    artifacts)               locks)
```

Full architecture specification: [`.github/instructions/architecture.instructions.md`](.github/instructions/architecture.instructions.md)

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

### Backend (planned)

| Technology | Purpose |
|---|---|
| ASP.NET Core (.NET) | API and orchestration runtime |
| Microsoft Agent Framework | Agent lifecycle management |
| PostgreSQL + pgvector | Persistent storage and semantic memory |
| Redis | Ephemeral round state, locks, rate limits |
| SignalR | Real-time streaming to frontend |

---

## Project Structure

```
agon/
├── .github/
│   └── instructions/           # System specs and coding rules
│       ├── architecture.instructions.md
│       ├── copilot.instructions.md
│       ├── prd-agon-core.instructions.md
│       ├── prompt-engineering-config.instructions.md
│       ├── round-policy.instructions.md
│       └── schemas.instructions.md
├── backend/                    # ASP.NET Core API (not yet implemented)
├── frontend/                   # Next.js application
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

### Install and Run

```bash
# Clone the repository
git clone https://github.com/simonholmes001/agon.git
cd agon

# Install frontend dependencies
cd frontend
npm install

# Start the development server
npm run dev
```

Open [http://localhost:3000](http://localhost:3000) to see the app.

### Available Scripts

From the `frontend/` directory:

| Command | Description |
|---|---|
| `npm run dev` | Start dev server with Turbopack |
| `npm run build` | Production build |
| `npm run start` | Start production server |
| `npm run lint` | Run ESLint |
| `npm run test` | Run all tests (Vitest) |
| `npm run test:watch` | Run tests in watch mode |

---

## Testing

Tests use [Vitest](https://vitest.dev) with [React Testing Library](https://testing-library.com/docs/react-testing-library/intro) and [jsdom](https://github.com/jsdom/jsdom).

```bash
# Run all tests
npm run test

# Run in watch mode
npm run test:watch

# Run with coverage report
npx vitest run --coverage
```

**Current coverage (63 tests across 7 test files):**

| Area | Statements | Branches | Functions | Lines |
|---|---|---|---|---|
| `lib/` | 100% | 100% | 100% | 100% |
| `components/landing/` | 100% | 100% | 100% | 100% |
| `components/session/` | 90% | 79% | 90% | 93% |
| **Overall** | **78%** | **76%** | **70%** | **79%** |

---

## Development Methodology

This project follows strict **Test-Driven Development (TDD)**:

1. **Red** — Write a failing test that describes the desired behaviour.
2. **Green** — Write the minimum code to make the test pass.
3. **Refactor** — Clean up while keeping tests green.

No production code without a failing test that motivated it.

See [`.github/instructions/copilot.instructions.md`](.github/instructions/copilot.instructions.md) for the full coding guidelines.

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
- [x] Component test suite
- [ ] Backend API — ASP.NET Core with session management
- [ ] Agent orchestration — Microsoft Agent Framework integration
- [ ] SignalR real-time streaming
- [ ] Truth Map persistence and patch validation
- [ ] Artifact generation pipeline

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
