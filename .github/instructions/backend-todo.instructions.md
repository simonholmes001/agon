---
applyTo: 'backend/**'
---
# Backend Implementation TODO

**Status as of March 6, 2026:** ~85% Complete  
**Current Test Status:** 168/171 passing (3 skipped)  
**Estimated remaining work:** 20-30 hours to v1 MVP

---

## Architecture Reference

**Source of Truth:** `architecture.instructions.md` (Version 3.0)

**Correct Agent Roster (5 agents total):**
1. **Moderator / Clarifier** - Orchestrates session, runs Socratic clarification
2. **GPT Agent** (OpenAI GPT-5.2) - Analysis perspective #1
3. **Gemini Agent** (Google Gemini 3) - Analysis perspective #2
4. **Claude Agent** (Anthropic Claude Opus 4.6) - Analysis perspective #3
5. **Synthesizer / Final Report** - Unifies outputs, scores convergence

✅ **All 5 agents are implemented and working correctly.**

---

## ✅ COMPLETED (March 5, 2026)

### Multi-Provider LLM Agent Configuration
- [x] OpenAI GPT-4o integration via MAF
- [x] Google Gemini 2.0 Flash integration via MAF
- [x] Anthropic Claude 3.5 Sonnet integration via MAF
- [x] DeepSeek-V3.2 integration capability
- [x] Microsoft Agent Framework (MAF) wrapper implementation
- [x] MafCouncilAgent adapter with AIAgent wrapping
- [x] Environment-aware agent registration (skips in Testing)
- [x] Agent system prompts for all 5 agents
- [x] Streaming response support via AIAgent.RunStreamingAsync

### Core Domain Logic
- [x] TruthMap model with all entity types
- [x] TruthMapPatch with PatchOperation (add/update/remove)
- [x] PatchValidator (schema validation, conflict detection)
- [x] ConfidenceDecayEngine (decay on challenge, boost on evidence)
- [x] ChangeImpactCalculator (derived_from graph traversal)
- [x] ConvergenceEvaluator (friction-adjusted thresholds, 7-dimension rubric)
- [x] RoundPolicy (budget limits, round limits, convergence thresholds)

### Application Layer
- [x] Orchestrator (deterministic state machine)
- [x] AgentRunner (context building, agent dispatch)
- [x] SessionService (session CRUD)
- [x] SnapshotService (round-end snapshots, fork support)
- [x] All application models (AgentContext, AgentResponse, DebateBrief, SessionState)

### Infrastructure Layer
- [x] PostgreSQL integration with EF Core
- [x] AgonDbContext with entity configurations
- [x] SessionRepository
- [x] TruthMapRepository (state + patch event log)
- [x] RedisSnapshotStore (ephemeral round state)
- [x] SignalREventBroadcaster (real-time streaming)

### API Layer
- [x] SessionsController (REST endpoints)
- [x] DebateHub (SignalR hub)
- [x] Dependency injection configuration
- [x] .env configuration for LLM API keys

### Testing
- [x] Domain layer tests: 66/66 ✅
- [x] Application layer tests: 56/56 ✅
- [x] Infrastructure layer tests: 39/42 ✅ (3 skipped - AIAgent mocking)
- [x] API layer tests: 7/7 ✅

---

## ❌ REMAINING WORK (Required for v1 MVP)

### 1. Memory / Retrieval Service (Semantic Memory)
**Priority:** HIGH  
**Estimated Effort:** 8-12 hours  
**Location:** `Agon.Infrastructure/Memory/`

**Requirements:**
- [ ] Install pgvector extension in PostgreSQL
  ```sql
  CREATE EXTENSION IF NOT EXISTS vector;
  
  CREATE TABLE truth_map_entity_embeddings (
      entity_id UUID PRIMARY KEY,
      session_id UUID NOT NULL,
      entity_type VARCHAR(50) NOT NULL,
      text TEXT NOT NULL,
      embedding vector(1536),  -- OpenAI ada-002 dimension
      created_at TIMESTAMP DEFAULT NOW()
  );
  
  CREATE INDEX ON truth_map_entity_embeddings USING ivfflat (embedding vector_cosine_ops);
  ```

- [ ] Create embedding pipeline for Truth Map entities
  - Embed claim text, assumption text, decision rationale, risk descriptions
  - Store embeddings in `truth_map_entity_embeddings` table
  - Run after each Truth Map patch is applied

- [ ] Implement semantic retrieval service
  ```csharp
  public interface IMemoryService
  {
      Task<IReadOnlyList<Memory>> GetTopKMemories(Guid sessionId, string query, int k, CancellationToken ct);
      Task IndexEntity(Guid sessionId, TruthMapEntity entity, CancellationToken ct);
      Task<IReadOnlyList<Memory>> QueryNaturalLanguage(Guid sessionId, string query, CancellationToken ct);
  }
  ```

- [ ] Integrate with AgentRunner
  - Inject top-K relevant memories into `AgentContext.SemanticMemories`
  - Support natural-language queries: "what did we decide about stack?", "find unresolved risks", "show contested claims"

**Dependencies:**
```xml
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.2.0" />
<PackageReference Include="Microsoft.SemanticKernel" Version="1.x.x" /> <!-- for embeddings -->
```

**Impact if missing:** Agents cannot retrieve relevant prior context from earlier rounds or related sessions. Each round operates in isolation without memory of past debate.

---

### 2. Budget Tracking Middleware
**Priority:** HIGH  
**Estimated Effort:** 4-6 hours  
**Location:** `Agon.Application/Services/BudgetTracker.cs`

**Requirements:**
- [ ] Track token usage per session in Redis
  ```csharp
  public interface IBudgetTracker
  {
      Task RecordUsage(Guid sessionId, int promptTokens, int completionTokens, string provider, CancellationToken ct);
      Task<BudgetStatus> GetStatus(Guid sessionId, CancellationToken ct);
      Task<bool> CanProceed(Guid sessionId, CancellationToken ct);
  }
  
  public record BudgetStatus(int TokensUsed, int TokensLimit, decimal PercentUsed, bool CanProceed);
  ```

- [ ] Middleware to intercept all LLM calls
  - Extract `usage` metadata from model responses (prompt_tokens, completion_tokens)
  - Increment session token counter in Redis
  - Check against tier budget limits (configured per session mode)

- [ ] Broadcast budget warnings via SignalR
  - At 80% usage: passive warning in session header
  - At 95% usage: prominent warning with options to continue or end session
  - SignalR event: `BudgetWarning { sessionId, percentUsed, tokensRemaining }`

- [ ] Hard stop at 100%
  - Prevent further agent calls
  - Trigger graceful degradation: transition to DELIVER_WITH_GAPS phase
  - Surface clear message to user explaining budget exhaustion

**Implementation notes:**
- Use Redis sorted set to track usage by timestamp (enables per-minute rate limiting if needed)
- Store per-provider costs for accurate billing calculation
- Budget limits should be configurable per session tier (quick mode vs deep mode)

**Impact if missing:** No cost control. Risk of runaway API costs in production, especially during testing or with misbehaving agents that loop.

---

### 3. Blob Storage Integration
**Priority:** MEDIUM  
**Estimated Effort:** 4-6 hours  
**Location:** `Agon.Infrastructure/Storage/BlobArtifactStore.cs`

**Requirements:**
- [ ] Azure Blob Storage client integration
  ```xml
  <PackageReference Include="Azure.Storage.Blobs" Version="12.22.0" />
  ```

- [ ] Artifact storage service (content-addressed)
  ```csharp
  public interface IArtifactStorageService
  {
      Task<Uri> StoreArtifact(Guid sessionId, string artifactType, string content, CancellationToken ct);
      Task<string> RetrieveArtifact(Guid sessionId, string artifactType, CancellationToken ct);
      Task<Uri> ExportSessionPack(Guid sessionId, CancellationToken ct);
  }
  ```

- [ ] Session export service
  - Package complete session: all artifacts + transcript + Truth Map snapshot + patch history
  - Return signed download URL (time-limited, e.g., 24 hours)
  - Export format: ZIP file with structured folders

- [ ] Content-addressed storage
  - Use SHA-256 hash of artifact content as blob key
  - Enables deduplication and immutability
  - Blob path: `{sessionId}/{artifactType}/{hash}.md`

**Artifact types to support:**
- `Verdict.md` - Go/No-Go decision with rationale
- `Plan.md` - Phased implementation plan (MVP/v1/v2)
- `PRD.md` - Formalized product requirements
- `Risk-Registry.md` - All identified risks with mitigation
- `Assumption-Validation.md` - All assumptions with validation steps
- `Architecture.mmd` - Mermaid diagram of proposed system
- `Copilot-Instructions.md` - Repo-specific development rules
- `Scenario-Diff.md` - (if forked session) comparison of key decisions

**Impact if missing:** Generated artifacts exist only in memory. Cannot deliver outputs to users or persist them for later retrieval.

---

### 4. Global Error Handling (Enhanced)
**Priority:** MEDIUM  
**Estimated Effort:** 3-4 hours  
**Location:** `Agon.Api/Middleware/EnhancedExceptionHandler.cs`

**Requirements:**
- [ ] Exception handling middleware (already exists, needs enhancement)
  - Catch all unhandled exceptions
  - Log with correlation IDs (trace entire request chain)
  - Return RFC 7807 Problem Details
  - Never expose stack traces or raw exceptions to users

- [ ] Provider error handling with retry logic
  ```csharp
  // In MafCouncilAgent or dedicated ProviderResiliencePolicy
  - Detect 5xx errors from LLM providers
  - Retry once after 5 second delay
  - If retry fails: skip agent for this round, log timeout
  - Surface user-friendly message: "Agent temporarily unavailable"
  ```

- [ ] Graceful degradation on timeouts
  - Agent timeout threshold: 90 seconds wall-clock per call
  - If agent times out: continue session with available responses
  - Log timeout event with agent_id and round context
  - Do NOT block entire session on single agent failure

- [ ] Structured logging enhancement
  - Use `ILogger<T>` throughout (already in place)
  - Never log raw user content or agent responses (privacy requirement)
  - Log structural events only:
    - `session_id`, `round`, `agent_id`, `patch_count`, `latency_ms`, `token_usage`
  - Add correlation IDs to trace requests across services

**Implementation:**
```csharp
public class EnhancedExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var correlationId = context.TraceIdentifier;
        _logger.LogError(exception, "Unhandled exception [CorrelationId: {CorrelationId}] at {Path}", 
            correlationId, context.Request.Path);
        
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An error occurred processing your request.",
            Detail = _hostEnvironment.IsDevelopment() 
                ? exception.Message 
                : "The development team has been notified.",
            Extensions = { ["correlationId"] = correlationId }
        };
        
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(problemDetails, ct);
        
        return true;
    }
}
```

**Impact if missing:** Limited resilience to provider failures. Poor visibility into production issues. User experience degrades silently rather than with clear feedback.

---

## 🧪 Testing the Backend (Current State)

### Can you test the backend now? **YES!**

The backend is **fully testable and functional** for local development despite the remaining work:

#### ✅ What Works for Testing:

1. **All Unit Tests Pass**
   ```bash
   cd backend
   dotnet test
   # Result: 168/171 passing (3 skipped - AIAgent mocking limitation)
   ```

2. **All Core Debate Logic Works**
   - Start a session
   - Run clarification with Moderator
   - Execute analysis round (all 3 agents in parallel)
   - Execute critique round
   - Run synthesis and convergence evaluation
   - Apply Truth Map patches
   - Run Confidence Decay Engine
   - Run Change Impact Calculator
   - Store everything in PostgreSQL + Redis

3. **Multi-Provider LLM Integration Works**
   - OpenAI GPT-4o calls (if API key in .env)
   - Anthropic Claude 3.5 Sonnet calls (if API key in .env)
   - Google Gemini 2.0 Flash calls (if API key in .env)
   - Real streaming responses via SignalR

4. **REST API Works**
   ```bash
   cd backend/src/Agon.Api
   dotnet run
   
   # Then test endpoints:
   curl -X POST http://localhost:5000/api/sessions \
     -H "Content-Type: application/json" \
     -d '{"idea": "Build a SaaS for remote teams", "mode": "quick", "frictionLevel": 50}'
   ```

#### ⚠️ What Doesn't Work Yet:

1. **No semantic memory** - Agents won't remember context from past rounds (each round is isolated)
2. **No cost controls** - No warnings when approaching budget limits
3. **No artifact delivery** - Generated outputs (Verdict, Plan, PRD) aren't persisted or downloadable
4. **Limited error resilience** - Provider failures may cause session to fail rather than degrade gracefully

#### 🎯 Recommended Testing Approach:

**Option 1: Unit Tests Only (No API Keys Required)**
```bash
cd backend
dotnet test
```
All tests use fake agents with canned responses. No real LLM calls.

**Option 2: Integration Testing (Requires API Keys)**
1. Create `.env` file in `backend/src/Agon.Api/`:
   ```env
   OPENAI_API_KEY=sk-...
   ANTHROPIC_API_KEY=sk-ant-...
   GOOGLE_API_KEY=AIza...
   ```

2. Start API:
   ```bash
   cd backend/src/Agon.Api
   dotnet run
   ```

3. Test a full session via REST API or Postman
   - POST `/api/sessions` - create session
   - POST `/api/sessions/{id}/start` - begin clarification
   - POST `/api/sessions/{id}/messages` - send clarification responses
   - GET `/api/sessions/{id}` - check session state
   - Connect to SignalR hub to watch real-time streaming

**Option 3: Frontend Integration (Requires Frontend Setup)**
- Start backend API
- Start Next.js frontend
- Test full user flow end-to-end

#### 💰 Cost Warning for Testing:

Since **budget tracking is not implemented yet**, be careful when testing with real API keys:
- Each full session (clarification → analysis → critique → synthesis) costs ~$0.10-0.30 depending on model choices
- If you test 10 sessions, expect ~$1-3 in API costs
- Recommendation: Test with unit tests first, then do 1-2 real API sessions to verify integration

#### 🔍 How to Monitor During Testing:

Without budget tracking, you can still monitor via:
1. **Terminal output** - see agent responses in real-time
2. **PostgreSQL logs** - check session state and patches
3. **Redis inspection** - check snapshot storage
4. **Provider dashboards** - check OpenAI/Anthropic/Google usage

---

1. **Budget Tracking Middleware** (4-6 hours)
2. **Memory / Retrieval Service** (8-12 hours)
3. **Global Error Handling** (3-4 hours)
4. **Blob Storage Integration** (4-6 hours)

**Total remaining:** 19-28 hours

---

## What's Working Now

- ✅ Multi-provider LLM integration (OpenAI, Anthropic, Google)
- ✅ All 5 agents configured and functional per architecture v3.0
- ✅ Full debate orchestration
- ✅ Truth Map state management with patch validation
- ✅ Confidence Decay Engine
- ✅ Change Impact Calculator
- ✅ Convergence Evaluator
- ✅ PostgreSQL + Redis persistence
- ✅ SignalR streaming
- ✅ 168/171 tests passing

---

## What's Missing

- ❌ Semantic memory (agents can't retrieve relevant prior context)
- ❌ Budget tracking (risk of runaway API costs)
- ❌ Global error handling
- ❌ Artifact storage
