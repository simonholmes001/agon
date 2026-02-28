# PR: `feature/backend-domain` → `main`

## Title

**feat(domain): Agon.Domain layer — Truth Map, engines, agents, sessions (134 tests, TDD)**

---

## Summary

Implements the complete **Domain layer** for the Agon backend using strict TDD (Red → Green → Refactor). This layer contains all core business logic with **zero framework dependencies** — pure C# with BCL types only, as required by the Clean Architecture rules.

The Domain layer gives the backend its vocabulary: entity types, validation rules, scoring engines, and agent configuration. It is the foundation that the Application layer (Orchestrator, AgentRunner) and Infrastructure layer (MAF, DB, SignalR) will build on.

---

## What's Included

### Domain Source (24 files)

| Area | Files | Description |
|---|---|---|
| **Agents** | `AgentId.cs`, `AgentSystemPrompts.cs`, `AgentConfig.cs` | Canonical agent identifiers (7 council + orchestrator + user), all 7 system prompt templates from the prompt-engineering spec, per-agent config (model provider, tokens, timeout, active phases) with `DefaultCouncil` |
| **TruthMap** | `TruthMapState.cs`, `TruthMapPatch.cs`, `PatchValidator.cs`, `ValidationResult.cs` | Aggregate root with factory + queries, patch operation model (add/replace/remove), 5 validation rules (entity existence, ID matching, cross-agent protection, decision rationale, assumption validation step) |
| **Entities** | 10 files in `TruthMap/Entities/` | Claim, Assumption, Decision, Risk, Evidence, OpenQuestion, Persona, Constraints, Convergence, ConfidenceTransition + all associated enums (ClaimStatus, AssumptionStatus, RiskCategory, Severity, Likelihood, ConvergenceStatus, ConfidenceTransitionReason) |
| **Engines** | `ConfidenceDecayEngine.cs`, `ConfidenceDecayConfig.cs`, `ChangeImpactCalculator.cs` | Confidence decay on undefended challenges, boost on evidence, clamp [0.0, 1.0], contested threshold. BFS graph traversal of `derived_from`/`supports`/`contradicts` for change impact sets |
| **Sessions** | `SessionPhase.cs`, `SessionMode.cs`, `SessionStatus.cs`, `RoundPolicy.cs`, `ConvergenceEvaluator.cs` | 9-phase state enum, loop termination conditions, budget exhaustion, friction-adjusted convergence thresholds (standard 0.75 / high-friction 0.85), rubric scoring across 7 dimensions |
| **Snapshots** | `SessionSnapshot.cs`, `ForkRequest.cs` | Immutable round-end snapshots with SHA-256 content hashing, fork request model for Pause-and-Replay |

### Test Suite (12 files, 134 tests)

| Test File | Tests | Covers |
|---|---|---|
| `SessionPhaseTests.cs` | 4 | Session enums (Phase, Mode, Status) |
| `AgentIdTests.cs` | 9 | Agent constants, `IsCouncilAgent`, `AllCouncil` |
| `AgentSystemPromptsTests.cs` | 11 | All 7 prompts contain role/patch rules, `GetPrompt()` dispatch |
| `AgentConfigTests.cs` | 12 | Config construction, defaults, `DefaultCouncil` (all 7 agents with correct model assignments) |
| `EntityTests.cs` | 18 | All entity types + enum completeness |
| `TruthMapTests.cs` | 12 | `TruthMapState` aggregate: factory, version, entity existence, claim lookup |
| `PatchValidatorTests.cs` | 17 | All 5 validation rules + multi-error + success paths |
| `ConfidenceDecayEngineTests.cs` | 15 | Decay, boost, clamping, contested threshold, custom config |
| `ChangeImpactCalculatorTests.cs` | 9 | BFS traversal, diamond deps, circular deps, cross-entity links |
| `RoundPolicyTests.cs` | 12 | Loop termination, budget exhaustion, threshold selection |
| `ConvergenceEvaluatorTests.cs` | 10 | Overall calculation, friction-adjusted evaluation, weak dimension identification |
| `SnapshotTests.cs` | 5 | SHA-256 hashing, snapshot immutability, fork request |

### Infrastructure & CI Changes

| File | Change |
|---|---|
| `backend/.gitignore` | **New** — excludes `bin/`, `obj/`, NuGet artifacts, IDE files |
| `backend/Agon.sln` | **New** — solution with `Agon.Domain` + `Agon.Domain.Tests` |
| `.github/workflows/ci.yaml` | **Modified** — backend job now captures test count via `outputs`, new `update-badges` job combines frontend + backend counts |
| `README.md` | **Modified** — xUnit badge added, test count updated to combined total |
| `.github/instructions/backlog.instructions.md` | **Modified** — moved completed domain items, added backend logging task, corrected `IChatClient` naming |
| `.github/instructions/backend-implementation.instructions.md` | **New** — MAF integration guide, solution structure, layer rules, NuGet strategy, `ICouncilAgent` spec |
| `.github/instructions/copilot.instructions.md` | **Modified** — minor updates for backend implementation references |

---

## Architecture Compliance

| Rule | Status |
|---|---|
| Domain has zero NuGet dependencies | ✅ Pure C# + BCL only |
| All entities carry `derived_from` references | ✅ Claims, Assumptions, Decisions, Risks |
| Every agent step outputs MESSAGE + PATCH | ✅ Enforced in prompt templates |
| Orchestrator is deterministic (no LLM-driven transitions) | ✅ `RoundPolicy` + `SessionPhase` are pure logic |
| Truth Map is source of truth (not transcript) | ✅ `TruthMapState` is the aggregate root |
| Patch-based updates with provenance | ✅ `PatchMeta` carries agent, round, reason |
| Confidence decay is auditable | ✅ `ConfidenceTransition` records every change |
| Snapshots are content-addressed | ✅ SHA-256 hash of Truth Map state |

---

## Testing Methodology

Strict TDD throughout — every type was written test-first:

1. **RED** — Write failing test(s) for the next type or behaviour
2. **GREEN** — Implement the minimum code to pass
3. **REFACTOR** — Clean up (e.g., `foreach` → LINQ, extract value objects)

No production code was written without a failing test that motivated it.

---

## Not in Scope

This branch is Domain-only. The following are explicitly deferred:

- Application layer (Orchestrator, AgentRunner, services, interfaces)
- Infrastructure layer (MAF integration, PostgreSQL, Redis, SignalR)
- API layer (endpoints, middleware)
- Logging (`ILogger<T>` — requires `Microsoft.Extensions.Logging`, belongs in Application+)

---

## How to Verify

```bash
cd backend
dotnet test --verbosity normal
# Expected: 134 tests, 0 failures, 0 skipped
```

---

## Stats

- **48 files changed** | **3,875 insertions** | **69 deletions**
- **24 source files** | **12 test files**
- **134 backend tests** | **154 frontend tests** | **288 total**
