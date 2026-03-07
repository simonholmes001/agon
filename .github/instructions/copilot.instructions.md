---
applyTo: '**'
---
# Agon: Coding Guidelines

**Version:** 2.0

> This file contains coding rules and implementation guidelines. For full system architecture, runtime responsibilities, data model, and API surface, see `architecture.instructions.md`. For agent prompts, see `prompt-engineering-config.instructions.md`. For JSON schemas, see `schemas.instructions.md`. For concrete backend implementation decisions (MAF integration, solution structure, layer rules, NuGet strategy), see `backend-implementation.instructions.md`.

---

## Project Overview

**Agon** is a living strategy room for agentic idea analysis. A council of specialist AI agents debates a user's idea in structured rounds, collaboratively building a shared "Truth Map" (structured state graph). The output is a decision-grade artifact pack.

**Stack:** Next.js frontend · ASP.NET Core (.NET) backend · Microsoft Agent Framework · PostgreSQL + pgvector + Redis + Blob storage · SignalR

**Task backlog:** See `backlog.instructions.md` — it is auto-injected into every session. Update it as tasks are completed or added.

---

## Engineering Principles (Non-Negotiable)

These principles apply to **all code** in the project — backend, frontend, tests, infrastructure.

1. **Clean Code.** All code must follow clean code best practices: meaningful names, small focused functions, single responsibility, no duplication, clear intent over clever tricks, minimal comments (code should be self-documenting).

2. **Clean Architecture.** Strict separation of concerns across layers. Dependencies point inward. Domain logic has zero framework dependencies. Infrastructure is always behind abstractions. See the Backend Coding Rules section and `architecture.instructions.md` for the specific layer definitions.

3. **Test-Driven Development (TDD).** Write tests first, then implement. The cycle is Red → Green → Refactor — always. No production code without a failing test that motivated it. Tests are first-class citizens, not afterthoughts.

4. **File naming conventions.** All YAML files must use the `.yaml` extension, never `.yml`.

5. **Logging and Error Handling.** All code must include sufficient logging for production debugging. Never swallow errors silently. Specific rules:
   - **Frontend:** Use the structured logger (`lib/logger.ts`), never bare `console.*` calls. Every page route must have a Next.js `error.tsx` error boundary. The root layout must have `global-error.tsx`. User-facing error messages must be friendly — never expose stack traces or raw errors. Log context (component name, action, relevant IDs) with every log call.
   - **Backend (.NET):** Use `ILogger<T>` via dependency injection — never `Console.Write*`. Log structural events only (session_id, round, agent_id, patch_count, latency). Never log raw user content, idea text, or agent responses in plaintext (see Security and Privacy Rules). All API endpoints must return appropriate HTTP error codes with problem details. Unhandled exceptions must be caught by global middleware and logged with correlation IDs.
   - **Both:** Every `catch` block must log the error before handling it. Every async boundary (API calls, SignalR connections, provider calls) must have explicit error handling with retry or graceful degradation.

---

## Architectural Hard Rules

These rules are non-negotiable and must be enforced in every PR.

1. **Truth Map is the source of truth.** The conversation transcript is provenance only. Every artifact, score, and output is generated from the Truth Map — never from the raw transcript.

2. **Every agent step outputs MESSAGE + PATCH.** Never mix these. Never generate an artifact directly from an agent's MESSAGE. Always go through the Truth Map.

3. **Orchestrator is a deterministic state machine.** LLM outputs CANNOT trigger state transitions. If you find yourself parsing an LLM response string to decide what phase to run next — stop. That logic belongs in the Orchestrator's state machine.

4. **Debates are bounded.** No indefinite loops. All round counts and token budgets are hard-capped. Degradation is graceful and surfaced to the user — never silent.

5. **Every entity carries `derived_from` references.** Required for the Change Impact Calculator. Never create an entity without considering its provenance.

---

## Backend Coding Rules (.NET)

Use Clean Architecture with three layers: **Domain** (no framework dependencies), **Application** (use-cases and orchestration), **Infrastructure** (adapters and I/O). See `architecture.instructions.md` for the full component breakdown.

Key rules:
- PostgreSQL repositories via EF Core or Dapper — choose one, do not mix.
- All domain types (`TruthMap`, `TruthMapPatch`, `RoundPolicy`, etc.) must have zero framework dependencies.
- Infrastructure concerns (DB, Redis, SignalR, HTTP clients) must never leak into Domain or Application layers.

---

## Model and Provider Rules

### Reference Documentation

Always consult the relevant provider documentation when building or modifying agent integrations, `IChatModelClient` implementations, or orchestration logic:

| Provider / Framework | Documentation |
|---|---|
| OpenAI (GPT-5.2 Thinking) | https://developers.openai.com/api/docs |
| Google Gemini 3 | https://ai.google.dev/gemini-api/docs |
| Anthropic Claude Opus 4.6 | https://platform.claude.com/docs/en/home |
| DeepSeek-V3.2 | https://api-docs.deepseek.com/ |
| Microsoft Agent Framework | https://github.com/microsoft/agent-framework |

### Rules

- All LLM calls go through `IChatClient` (from `Microsoft.Extensions.AI`, provided by MAF). The spec's original `IChatModelClient` name maps to this interface. Never call provider SDKs directly from use-cases. See `backend-implementation.instructions.md` §1.3 for details.
- Always pass to every model call:
  - `max_output_tokens` (per agent, configured in `AgentConfig`)
  - `reasoning_mode: high` (provider-specific parameter mapping handled in each client implementation)
  - Trace metadata: `session_id`, `agent_id`, `round_id`
- Never call real vendor APIs in unit tests. Use `FakeCouncilAgent` (Infrastructure layer) with canned responses. See `backend-implementation.instructions.md` §4.
- The Orchestrator may route low-stakes sub-tasks (patch formatting, question generation, summarisation) to a cheaper model. This routing logic lives in `AgentRunner`, not in domain code.

---

## Frontend Rules (Next.js)

- App Router + TypeScript. No Pages Router.
- Tailwind for styling. Use shadcn/ui component primitives. No other component library.
- Mobile-first. Thread View is the primary layout. Truth Map is a bottom-sheet drawer on mobile and a right-panel sidebar on desktop.
- Friction slider must be visible in the session header at all times during an active session.
- Stream agent outputs and Truth Map patch events in real time via SignalR. The UI must never appear "stuck" — always show a streaming progress indicator when an agent is working.
- Contested claims (confidence < 0.3) must have a visible warning treatment in the thread view.
- Pending Revalidation entities (after HITL constraint change) must be highlighted until resolved.
- Keep components under 250–300 lines. Refactor aggressively. No god components.
- Do not use `localStorage` or `sessionStorage` for session state. Use server state via SWR or React Query with the REST API.
- **Error boundaries:** Every route segment (`app/`, `app/session/[id]/`, `app/session/new/`) must have an `error.tsx`. The root must also have `global-error.tsx` and `not-found.tsx`.
- **Logging:** Use the structured logger (`lib/logger.ts`) — never bare `console.log/warn/error`. The logger is environment-aware (silent in tests, structured in dev/prod) and includes component context.

---

## Testing Requirements

**Minimum code coverage:** 80% line coverage across all projects (Domain, Application, Infrastructure, API). This is non-negotiable.

**How to check coverage:**
```bash
# Run tests with coverage collection
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Generate human-readable report
reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" \
  -targetdir:"./TestResults/coverage-report" \
  -reporttypes:"Html;TextSummary"

# View summary
cat ./TestResults/coverage-report/Summary.txt
```

**Current coverage status (as of March 7, 2026):**
- Overall: 76% line coverage (1400/1841 covered lines)
- Domain: 87.5% ✅
- Application: 85.3% ✅
- Infrastructure: 74.6% ⚠️ (below target - needs improvement)
- API: 49.4% ❌ (below target - needs significant improvement)

**Coverage improvement priorities:**
1. API layer (currently 49.4%) - add tests for Program.cs startup configuration and middleware
2. Infrastructure layer (currently 74.6%) - add integration tests for MafCouncilAgent, DebateHub, AgentResponseParserAdapter

**Unit tests** (must have, no exceptions):
- `PatchValidator` — schema validation, conflict detection, cross-agent text modification prevention
- `RoundPolicy` — loop termination conditions, budget exhaustion, early convergence
- `ConvergenceEvaluator` — rubric scoring, friction-adjusted thresholds
- `ConfidenceDecayEngine` — decay on challenge, boost on evidence, clamping, threshold flagging
- `ChangeImpactCalculator` — derived_from graph traversal, impact set correctness

**Integration tests** (must have):
- SignalR event ordering (patch events arrive after the patch is applied, not before)
- Truth Map consistency across rounds (version increments, no orphaned entity IDs)
- Fork creation and snapshot immutability (forked session does not modify parent)

**Snapshot tests** (nice to have):
- Generated artifact Markdown output (Verdict, Plan, PRD templates)

---

## Security and Privacy Rules

- Encrypt all stored idea content and artifact text at rest.
- Do not log raw idea content, user text, or agent responses in plaintext application logs. Log structural events (session_id, round, patch_count, latency) only.
- Per-session "no persistence" mode: when enabled, nothing is written to PostgreSQL or Blob. All state is ephemeral in Redis with a TTL. Surface this mode clearly in the UI.
- Session data is tenant-scoped. Never query across session boundaries without explicit multi-tenant isolation.

---

## Output and Artifact Rules

- Artifact Markdown must follow the templates defined in the artifact spec (Verdict, Plan, PRD, Risk Registry, Assumption Validation, Architecture Mermaid, Scenario Diff).
- No invented citations. If research tools are enabled, every factual claim in artifacts must trace to an evidence entry in the Truth Map.
- If research tools are disabled, artifacts must include a notice: "External claims in this output have not been independently verified."
- Contested claims (confidence < 0.3) must be flagged in the Verdict artifact with a note that they require validation before execution.

---

## What Not to Build (Scope Guards for v1)

- No team/multi-user workspaces (Phase 2)
- No file attachments or RAG over user documents (Phase 2)
- No SwiftUI iOS app (Phase 2)
- No simulation mode (Phase 2)
- No continuous/subscription mode (Phase 2)
- No custom agent configuration by end users (Phase 2)
- No Map View or Session Timeline Scrubber UI (Phase 1.5)
