---
applyTo: '**'
---
# Agon: Coding Guidelines

**Version:** 2.0

> This file contains coding rules and implementation guidelines. For full system architecture, runtime responsibilities, data model, and API surface, see `architecture.instructions.md`. For agent prompts, see `prompt-engineering-config.instructions.md`. For JSON schemas, see `schemas.instructions.md`. For concrete backend implementation decisions (MAF integration, solution structure, layer rules, NuGet strategy), see `backend-implementation.instructions.md`.

---

## Project Overview

**Agon** is a living strategy room for agentic idea analysis. A council of specialist AI agents debates a user's idea in structured rounds, collaboratively building a shared "Truth Map" (structured state graph). The output is a decision-grade artifact pack.

**Stack:** CLI (TypeScript + oclif + ink) · ASP.NET Core (.NET) backend · Microsoft Agent Framework · PostgreSQL + pgvector + Redis + Blob storage · SignalR/SSE

**Client Strategy:** CLI-first (Phase 1) → Web UI (Phase 2). Focus on terminal-native users, text artifacts, and rapid validation before investing in complex UI.

**Task backlog:** See `backlog.instructions.md` — it is auto-injected into every session. Update it as tasks are completed or added.

### Azure Infrastructure Naming References

When defining Azure resource names and abbreviations, use these official references:

- https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-naming?toc=%2Fazure%2Fazure-resource-manager%2Fmanagement%2Ftoc.json#example-azure-resource-names
- https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations
- https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules

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

## CLI Rules (TypeScript + oclif + ink)

**Phase 1 (Current):** CLI is the primary client for Agon.

### Framework and Libraries
- **CLI Framework:** oclif (command structure, plugin system, auto-help)
- **UI Library:** ink (React for terminal UIs)
- **HTTP Client:** axios for REST API calls
- **Streaming:** EventSource or polling for SSE (Server-Sent Events)
- **Markdown Rendering:** marked-terminal (render artifacts in terminal)
- **Configuration:** cosmiconfig (support `.agonrc` files)
- **Spinners/Progress:** ora or ink-spinner for async operations

### Command Structure

```bash
# Core workflow
agon start <idea>           # Create session + clarification
agon clarify                # Continue clarification (interactive)
agon status                 # Show current session status
agon show <artifact>        # Display artifact (verdict, plan, prd, risks, etc.)

# HITL interactions
agon challenge <claim-id>   # Challenge a specific claim
agon constraint <text>      # Add/modify constraint mid-debate
agon deepdive <entity-id>   # Force targeted deep dive

# Session management
agon sessions               # List all sessions
agon resume <session-id>    # Resume paused session
agon fork <session-id>      # Fork session from snapshot

# Configuration
agon config                 # Show current config
agon config set <key> <val> # Set config value (friction, research-tools, etc.)
```

### Code Organization

```
cli/
├── bin/
│   └── agon.ts                      # CLI entry point
├── src/
│   ├── commands/                    # oclif command classes
│   │   ├── start.ts
│   │   ├── clarify.ts
│   │   ├── show.ts
│   │   ├── challenge.ts
│   │   ├── constraint.ts
│   │   ├── deepdive.ts
│   │   ├── sessions.ts
│   │   ├── resume.ts
│   │   ├── fork.ts
│   │   ├── status.ts
│   │   └── config.ts
│   ├── api/                         # Backend API client
│   │   ├── agon-client.ts          # Main API wrapper
│   │   ├── session-service.ts
│   │   ├── artifact-service.ts
│   │   └── types.ts
│   ├── ui/                          # Terminal UI components
│   │   ├── spinner.tsx             # ink spinner component
│   │   ├── progress.tsx            # ink progress bar
│   │   ├── markdown.ts             # Markdown renderer
│   │   ├── question-prompt.tsx     # Interactive clarification UI
│   │   └── status-display.tsx      # Session status UI
│   ├── state/                       # Local state management
│   │   ├── session-manager.ts      # .agon/ directory management
│   │   └── config-manager.ts       # .agonrc file handling
│   └── utils/
│       ├── logger.ts
│       ├── error-handler.ts
│       └── formatter.ts
├── test/
│   ├── commands/                    # Command tests
│   └── api/                         # API client tests
├── package.json
├── tsconfig.json
└── README.md
```

### CLI-Specific Rules

1. **Local State Management:**
   - Store active session ID in `~/.agon/current-session`
   - Cache session data in `~/.agon/sessions/<session-id>.json`
   - Store config in `~/.agonrc` (YAML format)

2. **Streaming Strategy:**
   - Use spinners/progress bars instead of token-by-token streaming
   - Poll `/sessions/{id}` for phase transitions every 2 seconds during active debate
   - Show "Agent X is thinking..." with spinner
   - Display full agent response when round completes

3. **Error Handling:**
   - Always show friendly error messages (never raw stack traces)
   - Suggest recovery actions: `Run 'agon resume' to continue`
   - Log full errors to `~/.agon/logs/agon.log` for debugging

4. **Interactive Mode:**
   - Use inquirer.js or ink-text-input for interactive prompts
   - Clarification questions as numbered list with text input
   - Confirmation prompts for destructive actions (fork, clear session)

5. **Output Formatting:**
   - Use chalk for colors (green=success, red=error, yellow=warning, blue=info)
   - Use boxen for important messages
   - Use cli-table3 for tabular data (sessions list, Truth Map entities)
   - Render Markdown artifacts with marked-terminal

6. **Performance:**
   - Cache API responses locally (session state, artifacts)
   - Invalidate cache on state changes only
   - Lazy-load artifacts (don't fetch until `show` command)

7. **Testing:**
   - Mock API client in command tests
   - Test command parsing and flag handling
   - Test interactive prompt flows
   - Integration tests against real backend (optional)

### Example User Flow

```bash
# Start new session
$ agon start "Build a SaaS for project management"
🎯 Creating session...
✓ Session created: 8f3d2a1b

🤔 Moderator is analyzing your idea...

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Clarification (Round 1/2):

1. Who is the target customer?
2. What is the primary pain point?
3. What's your expected timeline?
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

? Answer 1: Freelancers
? Answer 2: Lose track of multiple client projects
? Answer 3: 6 months

✓ Clarification complete.

🔄 Starting debate (friction: 50)...
  ✓ GPT Agent
  ✓ Gemini Agent  
  ✓ Claude Agent

📊 Convergence: 0.78 / 0.75 ✅

✓ Debate complete! Artifacts ready.

Run 'agon show verdict' to view your decision.
```

---

## Frontend Rules (Next.js) - Phase 2 (Future)

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
