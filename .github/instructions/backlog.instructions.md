---
applyTo: '**'
---
# Agon — Task Backlog

> Agents: read this at session start. Update as tasks are completed or added. Move completed items to the Completed section. Keep entries concise — this file is injected into every agent context.

---

## In Progress

_(Nothing currently in progress)_

---

## Pending

### Infrastructure

- [ ] **Backend coverage badges** — Extend CI + `update-readme-badges.sh` to collect backend coverage across all backend test projects. Backend test count parsing now supports multi-project output; coverage aggregation is still pending.

### Backend (.NET)

- [ ] **Backend logging** — `ILogger<T>` via DI in Application, Infrastructure, and Api layers. Orchestrator: phase transitions, round progression, convergence scores, patch counts, budget consumption. AgentRunner: dispatch, latency, timeout, token usage. MafCouncilAgent: provider calls, streaming, error/retry. GlobalExceptionMiddleware: unhandled exceptions with correlation IDs. Never log raw user content or agent responses in plaintext.
- [ ] **Orchestrator state machine** — Deterministic phase transitions per `round-policy.instructions.md`. LLM outputs cannot trigger transitions.
- [ ] **`IChatClient` integration** — Provider-agnostic LLM calls via MAF's `IChatClient` (from `Microsoft.Extensions.AI`) + `FakeCouncilAgent` for tests. See `backend-implementation.instructions.md` §1.3.
- [ ] **MAF compliance audit (follow-up)** — Verify provider adapters, reasoning parameter mapping, and trace metadata (`session_id`, `agent_id`, `round_id`) are fully aligned with `copilot.instructions.md` §Model and Provider Rules.
- [ ] **SignalR event surface expansion** — Extend `/hubs/debate` beyond baseline `RoundProgress` + `TruthMapPatch` to full event set (tokens, convergence, budget warnings, artifacts).
- [ ] **REST API endpoints** — Expand beyond vertical-slice core (`POST /sessions`, `GET /sessions/{id}`, `POST /sessions/{id}/start`, `GET /sessions/{id}/truthmap`) to full surface in `architecture.instructions.md` §6.
- [ ] **PostgreSQL persistence** — Sessions, Truth Map (JSONB + normalised entities), append-only event log.
- [ ] **pgvector semantic memory** — Embedding pipeline + top-K retrieval for agent context.
- [ ] **Redis ephemeral state** — Round state, locks, rate limits.

### Frontend

- [ ] **Connect to real backend** — Replace demo/mock data with REST API + SignalR.
- [ ] **SignalR sequencing** — Keep SignalR for the following branch (or add after REST wiring is stable).
- [ ] **SignalR client integration expansion** — Stream agent tokens, patch deltas, convergence, and budget warnings in real time across thread + Truth Map UI.
- [ ] **Session Timeline Scrubber (Phase 1.5)** — Snapshot access, Pause-and-Replay forks.
- [ ] **Map View (Phase 1.5, desktop)** — Claim graph visualisation.

### Testing

- [ ] **Integration tests** — SignalR event ordering, Truth Map consistency, fork/snapshot immutability.
- [ ] **Snapshot tests** — Artifact Markdown template output.

---

## Completed

- [x] Frontend scaffold (Next.js 16 + React 19 + TypeScript + Tailwind v4 + shadcn/ui)
- [x] Landing page with gradient branding + theme toggle
- [x] Session UI components (thread view, agent message cards, message composer, session header, Truth Map drawer)
- [x] Light/dark theme (ThemeProvider + ThemeToggle)
- [x] Frontend test suite — 154 tests, 19 files
- [x] CI pipeline — GitHub Actions (frontend-tests + backend-tests with .NET auto-detect)
- [x] Pre-commit hook (frontend + backend, skips if no .NET project)
- [x] Automatic coverage badges (frontend only — backend pending)
- [x] Structured logger (`lib/logger.ts`) — environment-aware, component-scoped, never exposes raw user content
- [x] Error boundaries — `global-error.tsx`, `error.tsx`, `not-found.tsx`, `session/[id]/error.tsx`
- [x] Logging & error handling engineering principle (#5) added to copilot instructions
- [x] File naming convention — `.yaml` not `.yml`
- [x] CI fix — ANSI escape code stripping for reliable test count parsing in badge automation
- [x] CI fix — Shell operator precedence in badge commit step
- [x] PR #1 merged (`feature/frontend` → `main`)
- [x] Domain layer scaffold — `Agon.sln` with `Agon.Domain` (net9.0, zero deps) + `Agon.Domain.Tests` (xUnit + FluentAssertions)
- [x] Backend scaffold vertical slice — `Agon.Application`, `Agon.Infrastructure`, `Agon.Api` added with clean layer dependencies and in-memory adapters
- [x] Core backend session endpoints — `POST /sessions`, `GET /sessions/{id}`, `POST /sessions/{id}/start`, `GET /sessions/{id}/truthmap`
- [x] SignalR baseline — `/hubs/debate` mapped with session group join/leave and baseline events (`RoundProgress`, `TruthMapPatch`)
- [x] Frontend SignalR baseline — session route hub connection, round progress updates, reconnect + REST resync
- [x] Truth Map domain model — `TruthMapState`, `TruthMapPatch`, `PatchValidator` (5 validation rules), all entity types with `derived_from` / `challenged_by`
- [x] Domain entities — Claim, Assumption, Decision, Risk, Evidence, OpenQuestion, Persona, Constraints, Convergence, ConfidenceTransition + all enums
- [x] Confidence Decay Engine — decay on undefended challenges, boost on evidence, clamp [0.0, 1.0], contested threshold
- [x] Change Impact Calculator — BFS `derived_from`/`supports`/`contradicts` graph traversal → impact set
- [x] Round Policy — loop termination, budget exhaustion, friction-adjusted convergence thresholds
- [x] Convergence Evaluator — rubric scoring, overall calculation, weak dimension identification
- [x] Session Snapshot — immutable round-end snapshots with SHA-256 content hashing + ForkRequest
- [x] Agent system prompts — all 7 agent prompts from prompt-engineering-config spec
- [x] Agent config — per-agent model provider, model name, max tokens, timeout, active phases with `DefaultCouncil`
- [x] Agent identifiers — `AgentId` constants with `IsCouncilAgent` + `AllCouncil`
- [x] Session enums — `SessionPhase` (9 phases), `SessionMode`, `SessionStatus`
- [x] Backend test suite — 158 tests, TDD (Red → Green → Refactor)
- [x] CI updated — backend test count captured + combined frontend+backend badge in `update-badges` job
- [x] README badges — xUnit badge added, combined test count (frontend + backend)
