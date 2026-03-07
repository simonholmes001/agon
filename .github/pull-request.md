## PR: Backend Orchestrator Vertical Slice

### Summary
This PR introduces a backend vertical slice across `Agon.Application`, `Agon.Infrastructure`, and `Agon.Api`, following the rules in:
- `.github/instructions/architecture.instructions.md`
- `.github/instructions/backend-implementation.instructions.md`
- `.github/instructions/round-policy.instructions.md`
- `.github/instructions/backlog.instructions.md`

Implemented scope:
- Added new projects: `Agon.Application`, `Agon.Infrastructure`, `Agon.Api`
- Added deterministic orchestration/session core in Application layer:
  - `Orchestrator`
  - `AgentRunner`
  - `SessionService`
  - interface boundaries (`ICouncilAgent`, repositories, event broadcaster)
- Added Infrastructure adapters for vertical-slice execution:
  - `InMemorySessionRepository`
  - `InMemoryTruthMapRepository`
  - `FakeCouncilAgent`
  - `NoOpEventBroadcaster`
- Added API endpoints:
  - `POST /sessions`
  - `GET /sessions/{id}`
  - `POST /sessions/{id}/start`
  - `GET /sessions/{id}/truthmap`
- Updated `backend/Agon.sln` with all new projects
- Updated docs/workflow:
  - `README.md`
  - `.github/instructions/backlog.instructions.md`
  - `.github/workflows/update-badges.yaml` (backend test count parsing now sums multi-project output)

### TDD and Tests
All additions were built with TDD (red -> green -> refactor).

New test projects:
- `backend/tests/Agon.Application.Tests` (8 tests)
- `backend/tests/Agon.Infrastructure.Tests` (5 tests)
- `backend/tests/Agon.Api.Tests` (3 tests)

Validation results:
- Frontend hooks: `154` passing
- Backend total: `158` passing
  - Domain: `142`
  - Application: `8`
  - Infrastructure: `5`
  - API: `3`

### Scope Notes
This PR intentionally uses in-memory/fake Infrastructure adapters for the vertical slice.
Production integrations are deferred:
- MAF + `IChatClient` provider wiring
- PostgreSQL / Redis / Blob persistence
- SignalR hub implementation
- full API surface from architecture spec

### Follow-ups
- Expand API to full endpoint set in `.github/instructions/architecture.instructions.md` section 6
- Replace in-memory repositories with PostgreSQL/Redis implementations
- Add real event broadcasting via SignalR
- Connect frontend pages to these backend endpoints on the integration branch
