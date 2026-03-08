# Orchestrator Wiring Implementation Plan

**Created:** March 9, 2026  
**Status:** Ready to implement with full TDD  
**Estimated Time:** 4-6 hours

---

## Problem Statement

The backend has all the pieces but they're not connected:
- ✅ 5 Council Agents configured via MAF (Moderator, GPT, Gemini, Claude, Synthesizer)
- ✅ Orchestrator class with full phase transition logic
- ✅ AgentRunner that dispatches agents and parses responses
- ❌ SessionService.StartClarificationAsync ONLY changes phase - does NOT call agents
- ❌ SessionsController.SubmitMessage has TODO comment - does NOT process user messages

**Result:** CLI can create sessions and call `/start`, but NO DEBATE HAPPENS. No clarification questions are generated.

---

## How Clarification Phase Should Work

### Expected Flow:
1. User calls `POST /sessions/{id}/start`
2. SessionService calls Orchestrator
3. Orchestrator calls AgentRunner to run Moderator agent
4. Moderator analyzes the core idea and returns:
   - **MESSAGE:** Clarification questions (or "READY" if idea is clear)
   - **PATCH:** Initial Truth Map seeding (constraints, personas, etc.)
5. If Moderator asks questions:
   - Questions are stored and returned via `GET /sessions/{id}/clarification`
   - User answers via `POST /sessions/{id}/messages`
   - Moderator processes answers and either asks more questions (up to max rounds) or outputs "READY"
6. When "READY" is received:
   - Orchestrator transitions to ANALYSIS_ROUND
   - GPT, Gemini, Claude agents run in parallel
   - Continue debate flow...

### Current Reality:
- Step 1 ✅ works
- Step 2 ❌ doesn't happen (SessionService doesn't call Orchestrator)
- Steps 3-6 ❌ never occur

---

## Required Changes

### 1. SessionService → Orchestrator Connection

**File:** `backend/src/Agon.Application/Services/SessionService.cs`

**Current Code (lines 74-102):**
```csharp
public async Task StartClarificationAsync(
    Guid sessionId,
    CancellationToken cancellationToken = default)
{
    var state = await _sessionRepo.GetAsync(sessionId, cancellationToken);
    
    if (state is null)
    {
        throw new InvalidOperationException($"Session {sessionId} not found");
    }

    if (state.Phase != SessionPhase.Intake)
    {
        throw new InvalidOperationException(
            $"Cannot start clarification from phase {state.Phase}. Session must be in Intake phase.");
    }

    // Transition to Clarification phase
    state.Phase = SessionPhase.Clarification;
    await _sessionRepo.UpdateAsync(state, cancellationToken);

    // Broadcast phase transition event
    if (_broadcaster is not null)
    {
        await _broadcaster.SendRoundProgressAsync(
            state.SessionId,
            SessionPhase.Clarification.ToString(),
            state.Status.ToString(),
            cancellationToken);
    }
}
```

**Required Change:**
```csharp
public async Task StartClarificationAsync(
    Guid sessionId,
    CancellationToken cancellationToken = default)
{
    var state = await _sessionRepo.GetAsync(sessionId, cancellationToken);
    
    if (state is null)
    {
        throw new InvalidOperationException($"Session {sessionId} not found");
    }

    if (state.Phase != SessionPhase.Intake)
    {
        throw new InvalidOperationException(
            $"Cannot start clarification from phase {state.Phase}. Session must be in Intake phase.");
    }

    // Transition to Clarification phase
    state.Phase = SessionPhase.Clarification;
    await _sessionRepo.UpdateAsync(state, cancellationToken);

    // Broadcast phase transition event
    if (_broadcaster is not null)
    {
        await _broadcaster.SendRoundProgressAsync(
            state.SessionId,
            SessionPhase.Clarification.ToString(),
            state.Status.ToString(),
            cancellationToken);
    }

    // ⚡ NEW: Call Orchestrator to run Moderator agent
    await _orchestrator.RunModeratorAsync(state, cancellationToken);
}
```

**Dependencies Required:**
- Inject `Orchestrator` into `SessionService` constructor
- Create `Orchestrator.RunModeratorAsync()` method

---

### 2. Add Orchestrator.RunModeratorAsync() Method

**File:** `backend/src/Agon.Application/Orchestration/Orchestrator.cs`

**New Method to Add (after StartSessionAsync):**
```csharp
/// <summary>
/// Runs the Moderator agent for the Clarification phase.
/// The Moderator either asks clarifying questions or signals READY.
/// </summary>
public async Task RunModeratorAsync(SessionState state, CancellationToken cancellationToken)
{
    _logger?.LogInformation(
        "Session {SessionId}: Running Moderator for clarification", state.SessionId);

    var response = await _agentRunner.RunModeratorAsync(state, cancellationToken);

    // Apply the Moderator's patch (seeds Truth Map with initial brief data)
    if (response.HasPatch)
    {
        var patch = response.Patch!;
        var updated = state.TruthMap.ApplyPatch(patch);
        state.TruthMap = updated.TruthMap;
        state.Conflicts.AddRange(updated.Conflicts);
    }

    // Check if Moderator signaled READY
    if (response.Message.Contains("READY", StringComparison.OrdinalIgnoreCase))
    {
        _logger?.LogInformation(
            "Session {SessionId}: Moderator signaled READY → transitioning to ANALYSIS_ROUND",
            state.SessionId);

        // Extract Debate Brief from the response (stored in Truth Map via patch)
        var brief = ExtractDebateBrief(state.TruthMap);
        await SignalClarificationCompleteAsync(state, brief, cancellationToken);
    }
    else
    {
        // Moderator asked questions - increment clarification round count
        state.ClarificationRoundCount++;
        await _sessionService.UpdateAsync(state, cancellationToken);

        _logger?.LogInformation(
            "Session {SessionId}: Moderator asked clarification questions (round {Round})",
            state.SessionId,
            state.ClarificationRoundCount);

        // Check if max clarification rounds reached
        if (state.ClarificationRoundCount >= _policy.MaxClarificationRounds)
        {
            _logger?.LogWarning(
                "Session {SessionId}: Max clarification rounds reached - proceeding with partial brief",
                state.SessionId);

            var partialBrief = ExtractDebateBrief(state.TruthMap);
            await SignalClarificationTimedOutAsync(state, partialBrief, cancellationToken);
        }
    }
}

private DebateBrief ExtractDebateBrief(TruthMap truthMap)
{
    // Extract the debate brief from the Truth Map's current state
    // The Moderator's patch will have populated constraints, personas, success metrics
    return new DebateBrief(
        CoreIdea: truthMap.CoreIdea,
        Constraints: truthMap.Constraints,
        SuccessMetrics: truthMap.SuccessMetrics,
        PrimaryPersona: truthMap.Personas.FirstOrDefault(),
        OpenQuestions: truthMap.OpenQuestions.Select(q => q.Question).ToList()
    );
}
```

---

### 3. Add IAgentRunner.RunModeratorAsync() Method

**File:** `backend/src/Agon.Application/Orchestration/IAgentRunner.cs`

**Add Method:**
```csharp
/// <summary>
/// Runs the Moderator agent for the Clarification phase.
/// </summary>
Task<AgentResponse> RunModeratorAsync(
    SessionState state, 
    CancellationToken cancellationToken);
```

---

### 4. Implement AgentRunner.RunModeratorAsync()

**File:** `backend/src/Agon.Application/Orchestration/AgentRunner.cs`

**Add Method:**
```csharp
public async Task<AgentResponse> RunModeratorAsync(
    SessionState state,
    CancellationToken cancellationToken)
{
    var moderator = _agents.FirstOrDefault(a => a.AgentId == AgentId.Moderator);
    
    if (moderator is null)
    {
        throw new InvalidOperationException("Moderator agent not configured");
    }

    var context = new AgentContext(
        SessionId: state.SessionId,
        TruthMap: state.TruthMap,
        FrictionLevel: state.FrictionLevel,
        Phase: SessionPhase.Clarification,
        RoundNumber: state.ClarificationRoundCount,
        CritiqueTargetMessages: [],
        SemanticMemories: [],
        MicroDirective: null,
        ResearchToolsEnabled: false
    );

    try
    {
        var response = await moderator.RunAsync(context, cancellationToken);
        
        // Track token usage
        state.TokensUsed += response.TokensUsed;
        
        return response;
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, 
            "Moderator agent failed for session {SessionId}", state.SessionId);
        throw;
    }
}
```

---

### 5. Handle User Messages (Process Clarification Responses)

**File:** `backend/src/Agon.Api/Controllers/SessionsController.cs`

**Current Code (lines 116-145):**
```csharp
[HttpPost("{id}/messages")]
public async Task<IActionResult> SubmitMessage(
    [FromRoute] Guid id,
    [FromBody] MessageRequest request,
    CancellationToken cancellationToken)
{
    var sessionState = await _sessionService.GetAsync(id, cancellationToken);

    if (sessionState is null)
    {
        return NotFound(new { error = $"Session {id} not found" });
    }

    // TODO: Route message to Orchestrator for processing
    // For now, just acknowledge receipt
    _logger.LogInformation(
        "Received message for session {SessionId}: {Content}",
        id,
        request.Content);

    return Accepted();
}
```

**Required Change:**
```csharp
[HttpPost("{id}/messages")]
public async Task<IActionResult> SubmitMessage(
    [FromRoute] Guid id,
    [FromBody] MessageRequest request,
    CancellationToken cancellationToken)
{
    var sessionState = await _sessionService.GetAsync(id, cancellationToken);

    if (sessionState is null)
    {
        return NotFound(new { error = $"Session {id} not found" });
    }

    _logger.LogInformation(
        "Received message for session {SessionId}: {Content}",
        id,
        request.Content);

    // ⚡ NEW: Route to SessionService to process user message
    await _sessionService.ProcessUserMessageAsync(id, request.Content, cancellationToken);

    return Accepted();
}
```

---

### 6. Add SessionService.ProcessUserMessageAsync() Method

**File:** `backend/src/Agon.Application/Services/ISessionService.cs`

**Add Interface Method:**
```csharp
Task ProcessUserMessageAsync(
    Guid sessionId, 
    string message, 
    CancellationToken cancellationToken = default);
```

**File:** `backend/src/Agon.Application/Services/SessionService.cs`

**Implement Method:**
```csharp
public async Task ProcessUserMessageAsync(
    Guid sessionId,
    string message,
    CancellationToken cancellationToken = default)
{
    var state = await _sessionRepo.GetAsync(sessionId, cancellationToken);
    
    if (state is null)
    {
        throw new InvalidOperationException($"Session {sessionId} not found");
    }

    // Store user message in session history
    state.UserMessages.Add(new UserMessage(DateTime.UtcNow, message));

    // Route based on current phase
    switch (state.Phase)
    {
        case SessionPhase.Clarification:
            // User is responding to clarification questions
            await _orchestrator.RunModeratorAsync(state, cancellationToken);
            break;

        case SessionPhase.Deliver:
            // Post-delivery Q&A
            // TODO: Implement post-delivery question handling
            break;

        default:
            throw new InvalidOperationException(
                $"Cannot process user messages in phase {state.Phase}");
    }

    await _sessionRepo.UpdateAsync(state, cancellationToken);
}
```

---

### 7. Add UserMessage to SessionState

**File:** `backend/src/Agon.Domain/Sessions/SessionState.cs`

**Add Property:**
```csharp
/// <summary>
/// User messages submitted during clarification or post-delivery Q&A.
/// </summary>
public List<UserMessage> UserMessages { get; init; } = new();

/// <summary>
/// Record of a user message with timestamp.
/// </summary>
public record UserMessage(DateTime Timestamp, string Content);
```

---

### 8. Update AgentContext to Include User Messages

**File:** `backend/src/Agon.Application/Models/AgentContext.cs`

**Add Property:**
```csharp
/// <summary>
/// User messages from clarification responses.
/// </summary>
IReadOnlyList<string> UserMessages
```

**Update AgentRunner.RunModeratorAsync() to pass user messages:**
```csharp
var context = new AgentContext(
    SessionId: state.SessionId,
    TruthMap: state.TruthMap,
    FrictionLevel: state.FrictionLevel,
    Phase: SessionPhase.Clarification,
    RoundNumber: state.ClarificationRoundCount,
    CritiqueTargetMessages: [],
    SemanticMemories: [],
    MicroDirective: null,
    ResearchToolsEnabled: false,
    UserMessages: state.UserMessages.Select(m => m.Content).ToList()  // ⚡ NEW
);
```

---

### 9. Update MafCouncilAgent to Include User Messages in Prompt

**File:** `backend/src/Agon.Infrastructure/Agents/MafCouncilAgent.cs`

**Update BuildPrompt() Method:**
```csharp
private string BuildPrompt(AgentContext context)
{
    var sb = new StringBuilder();
    
    // ... existing prompt building ...
    
    // ⚡ NEW: Add user messages for clarification phase
    if (context.Phase == SessionPhase.Clarification && context.UserMessages.Any())
    {
        sb.AppendLine();
        sb.AppendLine("USER RESPONSES TO PREVIOUS CLARIFICATION QUESTIONS:");
        foreach (var msg in context.UserMessages)
        {
            sb.AppendLine($"- {msg}");
        }
        sb.AppendLine();
    }
    
    // ... rest of prompt ...
}
```

---

## TDD Implementation Order

### Phase 1: Infrastructure Setup (Write Tests First)
1. ✅ Write failing test: `SessionService_StartClarification_Should_CallOrchestrator()`
2. ✅ Write failing test: `Orchestrator_RunModerator_Should_CallAgentRunner()`
3. ✅ Write failing test: `Orchestrator_RunModerator_Should_TransitionToAnalysisWhenReady()`
4. ❌ Run tests → All fail (RED)
5. ✅ Inject Orchestrator into SessionService
6. ✅ Implement Orchestrator.RunModeratorAsync() stub
7. ❌ Run tests → Some pass (GREEN-ish)
8. ✅ Implement IAgentRunner.RunModeratorAsync()
9. ✅ Implement AgentRunner.RunModeratorAsync()
10. ✅ Run tests → All pass (GREEN)

### Phase 2: User Message Handling (Write Tests First)
1. ✅ Write failing test: `SessionService_ProcessUserMessage_Clarification_Should_CallOrchestrator()`
2. ✅ Write failing test: `Orchestrator_RunModerator_WithUserMessages_Should_IncludeInContext()`
3. ❌ Run tests → All fail (RED)
4. ✅ Add UserMessage to SessionState
5. ✅ Implement SessionService.ProcessUserMessageAsync()
6. ✅ Update AgentContext with UserMessages property
7. ✅ Update AgentRunner to pass UserMessages to context
8. ✅ Update MafCouncilAgent to include UserMessages in prompt
9. ✅ Run tests → All pass (GREEN)

### Phase 3: Controller Integration (Write Tests First)
1. ✅ Write failing test: `SessionsController_SubmitMessage_Should_CallSessionService()`
2. ❌ Run test → Fails (RED)
3. ✅ Update SessionsController.SubmitMessage() to call SessionService
4. ✅ Run test → Passes (GREEN)

### Phase 4: End-to-End Integration Test
1. ✅ Write integration test: `FullClarificationFlow_Should_GenerateQuestionsAndProcessAnswers()`
2. ❌ Run test → May fail (RED)
3. ✅ Fix any integration issues
4. ✅ Run test → Passes (GREEN)

### Phase 5: Manual Testing
1. Start Docker containers
2. Run backend API
3. Create session via CLI
4. Call `agon start` and verify clarification questions appear
5. Answer questions via CLI
6. Verify Moderator processes answers
7. Verify transition to ANALYSIS_ROUND when ready

---

## Test Files to Create/Update

1. **`backend/tests/Agon.Application.Tests/Services/SessionServiceTests.cs`**
   - `StartClarification_Should_CallOrchestrator()`
   - `ProcessUserMessage_Clarification_Should_CallOrchestrator()`

2. **`backend/tests/Agon.Application.Tests/Orchestration/OrchestratorTests.cs`**
   - `RunModerator_Should_CallAgentRunner()`
   - `RunModerator_Should_ApplyPatch()`
   - `RunModerator_Ready_Should_TransitionToAnalysis()`
   - `RunModerator_MaxRounds_Should_TimeOut()`
   - `RunModerator_WithUserMessages_Should_IncludeInContext()`

3. **`backend/tests/Agon.Application.Tests/Orchestration/AgentRunnerTests.cs`**
   - `RunModerator_Should_CallModeratorAgent()`
   - `RunModerator_Should_TrackTokens()`
   - `RunModerator_Should_PassUserMessages()`

4. **`backend/tests/Agon.Integration.Tests/SessionsControllerIntegrationTests.cs`**
   - `SubmitMessage_Clarification_Should_ProcessWithOrchestrator()`
   - `FullClarificationFlow_Should_Work()`

---

## Success Criteria

✅ All unit tests pass (171 + new tests)  
✅ All integration tests pass  
✅ `agon start` command generates clarification questions  
✅ User can answer questions via CLI  
✅ Moderator processes answers correctly  
✅ Session transitions to ANALYSIS_ROUND when ready  
✅ No regression in existing functionality

---

## Rollout Plan

1. **Commit 1:** Add Orchestrator injection + RunModeratorAsync stub (with tests)
2. **Commit 2:** Implement IAgentRunner.RunModeratorAsync() (with tests)
3. **Commit 3:** Add UserMessage handling (with tests)
4. **Commit 4:** Wire SessionsController to SessionService (with tests)
5. **Commit 5:** Integration tests + manual testing
6. **Commit 6:** Update documentation (STATUS.md, PROGRESS.md, backend-todo.instructions.md)

---

## Notes

- This plan follows the TDD requirement: "ALWAYS USE TDD"
- Each change has corresponding tests written FIRST
- Changes are incremental and testable at each step
- No breaking changes to existing functionality
- All existing 171 tests should continue passing
