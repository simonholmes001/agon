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

- [ ] **Backend coverage badges** — Extend CI + `update-readme-badges.sh` to collect backend test count + coverage once the .NET project exists. The `dotnet test --collect:"XPlat Code Coverage"` step already runs but output is not parsed.

### Backend (.NET)

- [ ] **Scaffold .NET backend** — ASP.NET Core in `backend/` with Clean Architecture (Domain, Application, Infrastructure). See `architecture.instructions.md`.
- [ ] **Truth Map domain model** — `TruthMap`, `TruthMapPatch`, entity types with `derived_from` / `challenged_by`. Zero framework dependencies in Domain.
- [ ] **Orchestrator state machine** — Deterministic phase transitions per `round-policy.instructions.md`. LLM outputs cannot trigger transitions.
- [ ] **`IChatModelClient` interface** — Provider-agnostic LLM abstraction + `IFakeChatModelClient` for tests.
- [ ] **Confidence Decay Engine** — Decay on undefended challenges, boost on evidence, clamp [0.0, 1.0].
- [ ] **Change Impact Calculator** — `derived_from` graph traversal → downstream impact set.
- [ ] **Snapshot Service** — Immutable round-end snapshots, content-addressed, `ForkSession` support.
- [ ] **SignalR hub (`/hubs/debate`)** — Streaming tokens, patches, convergence, round progress.
- [ ] **REST API endpoints** — Per `architecture.instructions.md` §6.
- [ ] **PostgreSQL persistence** — Sessions, Truth Map (JSONB + normalised entities), append-only event log.
- [ ] **pgvector semantic memory** — Embedding pipeline + top-K retrieval for agent context.
- [ ] **Redis ephemeral state** — Round state, locks, rate limits.

### Frontend

- [ ] **Connect to real backend** — Replace demo/mock data with REST API + SignalR.
- [ ] **SignalR client integration** — Stream agent tokens, patches, convergence in real time.
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
