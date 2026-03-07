---
applyTo: '**/*.cs'
---
# Agon Backend Implementation Guide

**Version:** 1.0

> This file documents concrete implementation decisions for the .NET backend. For system architecture, see `architecture.instructions.md`. For coding rules, see `copilot.instructions.md`. For schemas, see `schemas.instructions.md`. For agent prompts, see `prompt-engineering-config.instructions.md`.

---

## 1) Microsoft Agent Framework (MAF) Integration

### 1.1 What We Use from MAF

MAF (`1.0.0-rc1`) provides a provider-agnostic agent abstraction. Agon uses it for the **agent call layer only**.

| MAF Component | Agon Usage |
|---|---|
| `IChatClient` (from `Microsoft.Extensions.AI`) | Provider-agnostic LLM interface — replaces the spec's `IChatModelClient` |
| `Microsoft.Extensions.AI.OpenAI` | OpenAI provider adapter — `new OpenAIClient(key).GetChatClient(model).AsIChatClient()` |
| `Anthropic` (official SDK) | Anthropic provider adapter — `new AnthropicClient { ApiKey = key }.AsIChatClient(modelName)` |
| `Google_GenerativeAI.Microsoft` | Gemini provider adapter — `new GenerativeAIChatClient(apiKey, modelName)` (implements `IChatClient` directly) |
| Streaming via `GetStreamingResponseAsync()` | Built-in token streaming for real-time UI |

### 1.2 What We Do NOT Use from MAF

MAF's workflow engine (`AgentWorkflowBuilder`, `WorkflowBuilder`) is **not used** for orchestration. Rationale:

1. **Message-routing vs. patch-based communication.** MAF workflows pass `ChatMessage` lists between agents. Agon agents communicate via structured `TruthMapPatch` operations to a shared Truth Map — a fundamentally different pattern.
2. **No conditional phase transitions.** MAF's `BuildSequential`/`BuildConcurrent` patterns don't support our session phase state machine (INTAKE → CLARIFICATION → DEBATE_ROUND_1 → ...), targeted loops, or micro-rounds.
3. **Deterministic orchestration rule.** MAF's `GroupChatManager` uses an LLM to route between agents, which violates Architectural Hard Rule #3: "LLM outputs CANNOT trigger state transitions."
4. **Custom convergence and budget logic.** Our Orchestrator must run the Confidence Decay Engine, Change Impact Calculator, and convergence scoring after each round — none of which map to MAF workflow primitives.

### 1.3 `IChatClient` Replaces `IChatModelClient`

The existing spec references `IChatModelClient` as a custom abstraction. Since MAF already uses `IChatClient` from `Microsoft.Extensions.AI` — which is provider-agnostic and supports all our target providers — we use `IChatClient` directly. No need to invent a separate interface.

- OpenAI: `new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient()`
- Anthropic: `new AnthropicClient { ApiKey = apiKey }.AsIChatClient(modelName)` (extension in `Microsoft.Extensions.AI` namespace)
- DeepSeek: Uses OpenAI-compatible API via `OpenAIClient` with a custom `OpenAIClientOptions` endpoint
- Gemini: `new GenerativeAIChatClient(apiKey, modelName)` from `Google_GenerativeAI.Microsoft` — implements `IChatClient` directly

All references to `IChatModelClient` in the spec should be read as `IChatClient`.

---

## 2) Solution Structure

```
backend/
├── Agon.sln
├── src/
│   ├── Agon.Domain/                          ← Pure domain, ZERO framework dependencies
│   │   ├── Agon.Domain.csproj
│   │   ├── Agents/
│   │   │   ├── AgentId.cs                    ← Enum/constants for agent identifiers
│   │   │   └── AgentSystemPrompts.cs         ← ALL 7 agent system prompts (single file)
│   │   ├── TruthMap/
│   │   │   ├── TruthMap.cs                   ← The authoritative session state
│   │   │   ├── TruthMapPatch.cs              ← Patch operation model
│   │   │   ├── PatchOperation.cs             ← Individual add/replace/remove op
│   │   │   ├── PatchMeta.cs                  ← Agent, round, reason metadata
│   │   │   ├── PatchValidator.cs             ← 5 validation rules from schemas spec
│   │   │   └── Entities/
│   │   │       ├── Claim.cs
│   │   │       ├── Assumption.cs
│   │   │       ├── Decision.cs
│   │   │       ├── Risk.cs
│   │   │       ├── Evidence.cs
│   │   │       ├── OpenQuestion.cs
│   │   │       ├── Persona.cs
│   │   │       ├── Constraints.cs
│   │   │       ├── Convergence.cs
│   │   │       └── ConfidenceTransition.cs
│   │   ├── Sessions/
│   │   │   ├── SessionPhase.cs               ← Enum for all phase states
│   │   │   ├── SessionMode.cs                ← quick | deep
│   │   │   ├── SessionStatus.cs              ← active | paused | complete | forked | etc.
│   │   │   ├── RoundPolicy.cs               ← Loop limits, budget, convergence thresholds
│   │   │   └── ConvergenceEvaluator.cs       ← Rubric scoring, friction-adjusted thresholds
│   │   ├── Engines/
│   │   │   ├── ConfidenceDecayEngine.cs      ← Decay/boost/clamp/threshold logic
│   │   │   ├── ConfidenceDecayConfig.cs      ← Configuration value object
│   │   │   └── ChangeImpactCalculator.cs     ← derived_from graph traversal
│   │   └── Snapshots/
│   │       ├── SessionSnapshot.cs            ← Immutable round-end snapshot
│   │       └── ForkRequest.cs                ← Pause-and-Replay branch request
│   │
│   ├── Agon.Application/                     ← Use-cases and orchestration logic
│   │   ├── Agon.Application.csproj           ← References: Agon.Domain
│   │   ├── Orchestration/
│   │   │   ├── Orchestrator.cs               ← Deterministic state machine
│   │   │   └── AgentRunner.cs                ← Dispatches calls, parses MESSAGE + PATCH
│   │   ├── Interfaces/
│   │   │   ├── ICouncilAgent.cs              ← Our abstraction over MAF agents
│   │   │   ├── ITruthMapRepository.cs
│   │   │   ├── ISessionRepository.cs
│   │   │   ├── ISnapshotStore.cs
│   │   │   └── IEventBroadcaster.cs          ← Abstraction over SignalR
│   │   └── Services/
│   │       ├── SessionService.cs
│   │       └── SnapshotService.cs
│   │
│   ├── Agon.Infrastructure/                  ← MAF, DB, SignalR, external I/O
│   │   ├── Agon.Infrastructure.csproj        ← References: Agon.Application, MAF packages
│   │   ├── Agents/
│   │   │   ├── MafCouncilAgent.cs            ← ICouncilAgent, provider-agnostic via IChatClient
│   │   │   ├── FakeCouncilAgent.cs           ← Canned responses for unit/integration tests
│   │   │   └── AgentResponseParser.cs        ← Parses MESSAGE + PATCH sections from LLM output
│   │   ├── Persistence/
│   │   │   ├── PostgreSQL/                   ← EF Core or Dapper (choose one, do not mix)
│   │   │   ├── Redis/
│   │   │   └── Blob/
│   │   └── SignalR/
│   │       └── DebateHub.cs                  ← /hubs/debate
│   │
│   └── Agon.Api/                             ← ASP.NET Core host (thin — routing + DI only)
│       ├── Agon.Api.csproj                   ← References: Agon.Application, Agon.Infrastructure
│       ├── Program.cs
│       ├── Controllers/                      ← Or minimal API endpoints
│       └── Middleware/
│           └── GlobalExceptionMiddleware.cs   ← Correlation IDs, problem details
│
└── tests/
    ├── Agon.Domain.Tests/                    ← Unit tests for all domain logic
    │   ├── Agon.Domain.Tests.csproj          ← References: Agon.Domain only
    │   ├── TruthMap/
    │   │   └── PatchValidatorTests.cs
    │   ├── Sessions/
    │   │   ├── RoundPolicyTests.cs
    │   │   └── ConvergenceEvaluatorTests.cs
    │   └── Engines/
    │       ├── ConfidenceDecayEngineTests.cs
    │       └── ChangeImpactCalculatorTests.cs
    │
    ├── Agon.Application.Tests/               ← Unit tests for orchestration logic
    │   └── Agon.Application.Tests.csproj     ← References: Agon.Application + test doubles
    │
    └── Agon.Infrastructure.Tests/            ← Integration tests
        └── Agon.Infrastructure.Tests.csproj
```

---

## 3) Layer Dependency Rules

```
Agon.Api → Agon.Application, Agon.Infrastructure
Agon.Infrastructure → Agon.Application
Agon.Application → Agon.Domain
Agon.Domain → (nothing — zero external dependencies)
```

- **Domain** must have no NuGet package references (except `System.*` BCL types). No EF Core, no JSON framework, no MAF.
- **Application** defines interfaces (`ICouncilAgent`, `ITruthMapRepository`, etc.) that Infrastructure implements. Application never references Infrastructure.
- **Infrastructure** is the only layer that references MAF packages, database clients, SignalR, and HTTP clients.
- **Api** is a thin composition root — it wires DI and exposes endpoints. No business logic.

---

## 4) Agent Abstraction: `ICouncilAgent`

The Application layer defines `ICouncilAgent` as the boundary between orchestration logic and the MAF-backed agent implementations:

```csharp
// Application layer — Agon.Application/Interfaces/ICouncilAgent.cs
public interface ICouncilAgent
{
    string AgentId { get; }
    string ModelProvider { get; }
    Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken);
    IAsyncEnumerable<string> RunStreamingAsync(AgentContext context, CancellationToken cancellationToken);
}
```

**`AgentContext`** carries everything the agent needs:
- Current Truth Map (full state)
- Friction level (0–100)
- Round metadata (round number, phase, prior agents' claim summaries)
- Contested claims list
- Top-K semantic memories (when available)
- Any HITL micro-directives

**`AgentResponse`** contains:
- `Message` — the parsed MESSAGE section (Markdown)
- `Patch` — the parsed PATCH section (deserialized `TruthMapPatch`)
- `RawOutput` — the full agent output (for logging/debugging, never stored in plaintext)

The Infrastructure layer implements this as `MafCouncilAgent`, which:
1. Loads the system prompt from `AgentSystemPrompts` (Domain layer)
2. Composes the full prompt via `AgentPromptComposer.ComposePrompt()` (injects Truth Map, friction level, round metadata)
3. Calls `chatClient.GetResponseAsync()` or `chatClient.GetStreamingResponseAsync()` directly on the injected `IChatClient`
4. Parses the response into MESSAGE + PATCH sections via `AgentResponseParser`

For tests, `FakeCouncilAgent` returns canned MESSAGE + PATCH responses without calling any LLM.

---

## 5) Centralized System Prompts

All 7 agent system prompt templates live in a single Domain-layer file:

```
backend/src/Agon.Domain/Agents/AgentSystemPrompts.cs
```

This is a static class with one `const string` or `static readonly string` property per agent. The prompt text comes from `prompt-engineering-config.instructions.md`. The Infrastructure-layer `MafCouncilAgent` imports these constants and interpolates session-specific context (Truth Map, friction level, round metadata) before each call.

Why in Domain:
- Prompts define agent behaviour — they are core business rules.
- Domain has zero framework dependencies, so prompts remain portable.
- Single file makes maintenance and cross-agent consistency easy.
- Changes to prompts are immediately visible in version control diffs.

---

## 6) NuGet Package Strategy

### Domain — zero packages
No NuGet references. Pure C# with BCL types only.

### Application — minimal
- Reference to `Agon.Domain` project only.
- No third-party packages (interfaces are defined here; implementations are in Infrastructure).

### Infrastructure
| Package | Purpose |
|---|---|
| `Microsoft.Extensions.AI.OpenAI` | OpenAI provider adapter (`IChatClient` implementation) |
| `Anthropic` | Official Anthropic SDK with `.AsIChatClient()` extension |
| `Google_GenerativeAI.Microsoft` | Gemini adapter — `GenerativeAIChatClient` implements `IChatClient` |
| `Microsoft.Extensions.AI` | `IChatClient` abstraction (pulled in transitively) |
| `Microsoft.AspNetCore.SignalR` | Real-time streaming hub |
| `Npgsql.EntityFrameworkCore.PostgreSQL` or `Dapper` | PostgreSQL persistence (choose one) |
| `StackExchange.Redis` | Redis ephemeral state |
| `Azure.Storage.Blobs` | Blob storage for exports/snapshots |
| `Pgvector` | pgvector extension for semantic memory |

### Api
| Package | Purpose |
|---|---|
| `Microsoft.AspNetCore.OpenApi` | API documentation |
| `Swashbuckle.AspNetCore` | Swagger UI (dev only) |

### Tests
| Package | Purpose |
|---|---|
| `xunit` | Test framework |
| `FluentAssertions` | Readable assertions |
| `NSubstitute` or `Moq` | Mocking (for Application-layer tests) |
| `Microsoft.NET.Test.Sdk` | Test runner |

---

## 7) Concurrency and Performance Strategy

This section defines how the backend achieves maximum throughput for the two parallel phases (ANALYSIS_ROUND and CRITIQUE). All concurrency decisions are made in `AgentRunner` (Application layer). The Domain and Infrastructure layers have no concurrency concerns — they are called by `AgentRunner`.

---

### 7.1 Parallel Agent Dispatch — `Task.WhenAll`

For both ANALYSIS_ROUND and CRITIQUE, `AgentRunner` dispatches all agent calls concurrently using `Task.WhenAll`. Each call is independent and I/O-bound (waiting on remote LLM API), so true parallelism is achieved with no thread-blocking:

```csharp
// AgentRunner.cs — Application layer
var tasks = agents.Select(agent =>
    agent.RunAsync(BuildContext(agent, truthMap, phase, critiqueTargets), cancellationToken)
).ToList();

AgentResponse[] responses = await Task.WhenAll(tasks);
```

- Each `RunAsync` call is a single `await` on an HTTP streaming response — no threads are blocked while waiting for LLM tokens.
- `CancellationToken` is wired through to every provider call so timeouts or user cancellations propagate cleanly.
- Individual agent failures (timeouts, 5xx errors) are caught per-task inside `RunAsync` and converted to a `TimedOutAgentResponse` — they do not cancel the other in-flight tasks.

---

### 7.2 Streaming Token Fan-Out — `Channel<T>`

Each agent streams tokens to the UI in real time while the Orchestrator waits for the full response before applying the PATCH. These two concerns are decoupled using `System.Threading.Channels`:

```csharp
// MafCouncilAgent.cs — Infrastructure layer
var channel = Channel.CreateUnbounded<string>();

// Producer: stream tokens from IChatClient onto the channel
_ = Task.Run(async () =>
{
    await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken))
    {
        await channel.Writer.WriteAsync(update.Text, cancellationToken);
        await broadcaster.SendTokenAsync(agentId, update.Text, cancellationToken);
    }
    channel.Writer.Complete();
});

// Consumer: accumulate full response for patch parsing
var sb = new StringBuilder();
await foreach (var token in channel.Reader.ReadAllAsync(cancellationToken))
    sb.Append(token);

return AgentResponseParser.Parse(sb.ToString());
```

This means:
- **The UI sees tokens immediately** as the LLM produces them (low time-to-first-token).
- **The PATCH is only parsed and applied after the full response is complete** (correctness guarantee).
- No polling. No intermediate buffering in Redis.

---

### 7.3 Patch Application — Serialised After `Task.WhenAll`

Patches from parallel agents must be applied to the Truth Map in a **deterministic order** (alphabetical by agent_id) and **one at a time** — concurrent writes to the Truth Map are not allowed. After `Task.WhenAll` returns, `AgentRunner` applies patches sequentially:

```csharp
// Patches are ordered deterministically before any are applied
var orderedResponses = responses
    .Where(r => r.Patch is not null)
    .OrderBy(r => r.AgentId, StringComparer.Ordinal);

foreach (var response in orderedResponses)
{
    var validationResult = patchValidator.Validate(response.Patch!, currentTruthMap);
    if (validationResult.IsValid)
        currentTruthMap = truthMapRepository.Apply(response.Patch!);
    else
        logger.LogWarning("Patch rejected for agent {AgentId}: {Reason}", response.AgentId, validationResult.Reason);
}
```

- There is no concurrent write contention because patch application happens **after** `Task.WhenAll` completes, not during streaming.
- A `SemaphoreSlim(1, 1)` guards the Truth Map write in any code path where patches could arrive out-of-order (e.g., HITL micro-rounds overlapping with a normal round completing).

---

### 7.4 Per-Agent Timeout — `CancellationTokenSource.CreateLinkedTokenSource`

Each agent call has its own timeout, independent of the others, using a linked cancellation token:

```csharp
using var agentTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(agentTimeoutSeconds));
using var linked = CancellationTokenSource.CreateLinkedTokenSource(agentTimeout.Token, sessionCancellationToken);

try
{
    return await agent.RunAsync(context, linked.Token);
}
catch (OperationCanceledException) when (agentTimeout.IsCancellationRequested)
{
    logger.LogWarning("Agent {AgentId} timed out after {Timeout}s", agent.AgentId, agentTimeoutSeconds);
    return AgentResponse.TimedOut(agent.AgentId);
}
```

- A single slow agent does not block or cancel the others.
- The session-level `CancellationToken` (budget exhaustion, user cancel) is also honoured via the linked token.

---

### 7.5 Token Budget Tracking — Atomic Counter in Redis

The session token budget is a shared mutable value across concurrent agent calls. It is maintained as an atomic counter in Redis using `StringIncrementAsync`:

```csharp
// After each agent response is received
var tokensUsed = response.TokensUsed; // from provider usage metadata
var newTotal = await redis.StringIncrementAsync($"budget:{sessionId}", tokensUsed);

if (newTotal >= budgetExhaustedThreshold)
    await orchestrator.TriggerBudgetExhausted(sessionId);
else if (newTotal >= budgetWarning95Threshold)
    await broadcaster.SendBudgetWarning(sessionId, newTotal, maxTokens);
```

- `StringIncrementAsync` is atomic — no race condition when multiple agent responses arrive close together.
- Each task increments independently as it completes; no need to wait for all tasks to finish before checking the budget.

---

### 7.6 Critique Target Injection — Computed Before Dispatch

Before dispatching the CRITIQUE parallel calls, `AgentRunner` pre-computes `critique_targets` for each agent and stamps them into the `AgentContext`. This is pure deterministic logic — no I/O, no LLM:

```csharp
// AgentRunner.cs
private static IReadOnlyList<string> GetCritiqueTargets(string agentId, IReadOnlyList<string> allAgentIds)
    => allAgentIds.Where(id => id != agentId).OrderBy(id => id).ToList();
```

Each agent's `AgentContext.CritiqueTargets` filters which Analysis Round MESSAGEs are included in its context window — the agent only receives the MESSAGEs of its two assigned targets, never its own.

---

### 7.7 Context Window Trimming — Semantic Memory Pre-Fetch

For sessions with large Truth Maps, context window size can become a bottleneck. The Memory/Retrieval Service (pgvector) pre-fetches top-K relevant memories for each agent call **in parallel with** the Truth Map serialisation step, so memory retrieval does not add to wall-clock latency:

```csharp
var (serialisedTruthMap, memories) = await (
    Task.Run(() => JsonSerializer.Serialize(truthMap)),
    memoryService.RetrieveAsync(sessionId, agentQuery, topK: 5, cancellationToken)
);
```

Only memories relevant to the agent's assigned role are injected — the GPT Agent does not receive memories flagged as architecture-specific, for example. This keeps each agent's context window tight and reduces LLM latency.

---

### 7.8 Concurrency Rules Summary

| Rule | Where enforced |
|---|---|
| Parallel dispatch via `Task.WhenAll` | `AgentRunner` (Application layer) |
| Token streaming via `Channel<T>` | `MafCouncilAgent` (Infrastructure layer) |
| Patch application is always sequential and deterministic | `AgentRunner` after `Task.WhenAll` |
| `SemaphoreSlim(1,1)` guards Truth Map writes for HITL micro-rounds | `AgentRunner` |
| Per-agent timeout via linked `CancellationTokenSource` | `AgentRunner` per task |
| Budget counter is atomic in Redis (`StringIncrementAsync`) | `AgentRunner` post-response |
| Critique targets computed deterministically before dispatch | `AgentRunner` pre-dispatch |
| Memory pre-fetch runs in parallel with Truth Map serialisation | `AgentRunner` pre-dispatch |
| No shared mutable state between concurrent agent calls | Enforced by architecture — agents receive immutable snapshots of the Truth Map |

---

## 8) Feature Branch Scope: `feature/backend-domain`

This branch implements the Domain layer with full TDD coverage. No infrastructure, no database, no API.

### Deliverables

1. **Solution scaffold** — `Agon.sln` with all projects and correct references
2. **Domain entities** — all types from `schemas.instructions.md` (TruthMap, Claims, Risks, etc.)
3. **`AgentSystemPrompts.cs`** — all 7 agent prompts from `prompt-engineering-config.instructions.md`
4. **`PatchValidator`** — the 5 validation rules:
   - Reject patches referencing non-existent entity IDs (unless `op` is `add`)
   - Reject `replace`/`remove` on mismatched entity `id`
   - Prevent cross-agent text modification (agents can update own claims; can add `challenged_by` but not overwrite others' text)
   - Require `rationale` on decisions
   - Require `validation_step` on assumptions after Round 2
5. **`RoundPolicy`** — loop termination, budget exhaustion, early convergence
6. **`ConvergenceEvaluator`** — rubric scoring, friction-adjusted thresholds
7. **`ConfidenceDecayEngine`** — decay on undefended challenge, boost on evidence, clamp [0.0, 1.0], contested threshold flagging
8. **`ChangeImpactCalculator`** — `derived_from` graph traversal → impact set
9. **`SessionPhase` enum** — all phase states from `round-policy.instructions.md`
10. **Full test suite** — tests written FIRST (TDD), covering all domain logic

### Not in scope for this branch
- Application layer (Orchestrator, AgentRunner, services)
- Infrastructure layer (MAF integration, database, SignalR)
- API layer (endpoints, middleware)
- Any NuGet packages beyond BCL
