using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Domain.Engines;
using Agon.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Agon.Application.Orchestration;

/// <summary>
/// Deterministic session state machine. Controls all phase transitions.
///
/// Architectural Hard Rules enforced here:
/// - LLM outputs NEVER trigger phase transitions — only Orchestrator logic does.
/// - Phase transitions follow a strict sequence (defined in round-policy spec).
/// - Budget exhaustion always terminates with DELIVER_WITH_GAPS, never by throwing.
///
/// The Orchestrator holds no mutable state — all state is in <see cref="SessionState"/>.
/// </summary>
public sealed class Orchestrator : IOrchestrator
{
    private static readonly Regex ModeratorStatusRegex = new(
        @"^\s*(?:status|clarification_status)\s*[:=]\s*(DIRECT_ANSWER|READY|NEEDS_INFO)\b",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IAgentRunner _agentRunner;
    private readonly ISessionService _sessionService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RoundPolicy _policy;
    private readonly ConfidenceDecayEngine _decayEngine;
    private readonly ILogger<Orchestrator>? _logger;

    public Orchestrator(
        IAgentRunner agentRunner,
        ISessionService sessionService,
        IServiceScopeFactory scopeFactory,
        RoundPolicy policy,
        ILogger<Orchestrator>? logger = null)
    {
        _agentRunner = agentRunner;
        _sessionService = sessionService;
        _scopeFactory = scopeFactory;
        _policy = policy;
        _decayEngine = new ConfidenceDecayEngine(policy.ConfidenceDecay);
        _logger = logger;
    }

    // ── Phase entry points ────────────────────────────────────────────────────

    /// <summary>
    /// Called when a new session is initiated (INTAKE).
    /// Seeds the Truth Map and transitions to CLARIFICATION.
    /// </summary>
    public async Task StartSessionAsync(SessionState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Session {SessionId}: INTAKE → CLARIFICATION", state.SessionId);
        await _sessionService.AdvancePhaseAsync(state, SessionPhase.Clarification, cancellationToken);
    }

    /// <summary>
    /// Runs the Moderator agent for the Clarification phase.
    /// The Moderator either asks clarifying questions or signals READY.
    /// </summary>
    public async Task RunModeratorAsync(SessionState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Session {SessionId}: Running Moderator for clarification", state.SessionId);

        var response = await _agentRunner.RunModeratorAsync(state, cancellationToken);
        var decision = ParseModeratorDecision(response.Message);
        var isFirstRound = state.ClarificationRoundCount == 0;
        var hasUserClarificationMessages = state.UserMessages.Count > 0;
        var shouldForceClarificationRound =
            isFirstRound
            && !hasUserClarificationMessages
            && decision.Status == ModeratorDecisionStatus.Ready;
        var shouldTreatReadyAsDirectAnswer =
            decision.Status == ModeratorDecisionStatus.Ready
            && ModeratorRoutingClassifier.ShouldForceDirectAnswer(state);

        if (shouldTreatReadyAsDirectAnswer)
        {
            _logger?.LogInformation(
                "Session {SessionId}: Moderator returned READY for a direct-answer query. Treating as DIRECT_ANSWER and suppressing debate chain.",
                state.SessionId);
            decision = new ModeratorDecision(ModeratorDecisionStatus.DirectAnswer);
        }

        if (decision.Status == ModeratorDecisionStatus.DirectAnswer)
        {
            _logger?.LogInformation(
                "Session {SessionId}: Direct-answer path selected. Remaining in Clarification phase.",
                state.SessionId);
            return;
        }

        if (decision.Status == ModeratorDecisionStatus.Ready && !shouldForceClarificationRound)
        {
            _logger?.LogInformation(
                "Session {SessionId}: Moderator signaled READY via structured status (round {Round})",
                state.SessionId,
                state.ClarificationRoundCount);

            var brief = ExtractDebateBrief(state.TruthMap);
            await SignalClarificationCompleteAsync(state, brief, cancellationToken);
            return;
        }

        if (decision.Status == ModeratorDecisionStatus.Unknown)
        {
            _logger?.LogWarning(
                "Session {SessionId}: Moderator response missing structured STATUS marker. Falling back to clarification flow.",
                state.SessionId);
        }

        if (shouldForceClarificationRound)
        {
            _logger?.LogInformation(
                "Session {SessionId}: Ignoring premature READY on first clarification round.",
                state.SessionId);
        }

        // Moderator requested more information (or response was unstructured).
        state.ClarificationRoundCount++;
        await _sessionService.RecordRoundSnapshotAsync(state, cancellationToken);

        _logger?.LogInformation(
            "Session {SessionId}: Moderator requested more clarification (round {Round})",
            state.SessionId,
            state.ClarificationRoundCount);

        if (state.ClarificationRoundCount >= _policy.MaxClarificationRounds)
        {
            _logger?.LogWarning(
                "Session {SessionId}: Max clarification rounds reached - proceeding with partial brief",
                state.SessionId);

            var partialBrief = ExtractDebateBrief(state.TruthMap);
            await SignalClarificationTimedOutAsync(state, partialBrief, cancellationToken);
        }
    }

    /// <summary>
    /// Runs a single-agent post-delivery follow-up response.
    /// If the session is still in Deliver/DeliverWithGaps, it is transitioned to PostDelivery.
    /// </summary>
    public async Task RunPostDeliveryFollowUpAsync(
        SessionState state,
        string userMessage,
        CancellationToken cancellationToken)
    {
        if (state.Phase is SessionPhase.Deliver or SessionPhase.DeliverWithGaps)
        {
            await _sessionService.AdvancePhaseAsync(state, SessionPhase.PostDelivery, cancellationToken);
        }

        _logger?.LogInformation(
            "Session {SessionId}: Running post-delivery follow-up assistant",
            state.SessionId);

        await _agentRunner.RunPostDeliveryFollowUpAsync(state, userMessage, cancellationToken);
    }

    private static DebateBrief ExtractDebateBrief(Domain.TruthMap.TruthMap truthMap)
    {
        // Extract the debate brief from the Truth Map's current state
        // The Moderator's patch will have populated constraints, personas, success metrics
        var constraints = truthMap.Constraints is not null
            ? new BriefConstraints(
                truthMap.Constraints.Budget,
                truthMap.Constraints.Timeline,
                truthMap.Constraints.TechStack,
                truthMap.Constraints.NonNegotiables)
            : new BriefConstraints(string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<string>());

        var primaryPersona = truthMap.Personas.FirstOrDefault();
        var personaName = primaryPersona?.Name ?? string.Empty;

        var openQuestions = truthMap.OpenQuestions
            .Select(q => q.Text)
            .ToList();

        return new DebateBrief(
            CoreIdea: truthMap.CoreIdea,
            Constraints: constraints,
            SuccessMetrics: truthMap.SuccessMetrics,
            PrimaryPersona: personaName,
            OpenQuestions: openQuestions
        );
    }

    private static ModeratorDecision ParseModeratorDecision(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new ModeratorDecision(ModeratorDecisionStatus.Unknown);
        }

        var statusMatch = ModeratorStatusRegex.Match(message);
        if (statusMatch.Success)
        {
            var rawStatus = statusMatch.Groups[1].Value.Trim().ToUpperInvariant();
            return rawStatus switch
            {
                "DIRECT_ANSWER" => new ModeratorDecision(ModeratorDecisionStatus.DirectAnswer),
                "READY" => new ModeratorDecision(ModeratorDecisionStatus.Ready),
                "NEEDS_INFO" => new ModeratorDecision(ModeratorDecisionStatus.NeedsInfo),
                _ => new ModeratorDecision(ModeratorDecisionStatus.Unknown)
            };
        }

        return new ModeratorDecision(ModeratorDecisionStatus.Unknown);
    }

    private enum ModeratorDecisionStatus
    {
        Unknown,
        DirectAnswer,
        NeedsInfo,
        Ready
    }

    private sealed record ModeratorDecision(ModeratorDecisionStatus Status);

    /// <summary>
    /// Called when the Moderator signals READY at the end of the Clarification phase.
    /// Stores the completed Debate Brief and fires the full debate chain in background.
    /// </summary>
    public async Task SignalClarificationCompleteAsync(
        SessionState state,
        DebateBrief brief,
        CancellationToken cancellationToken)
    {
        state.DebateBrief = brief;
        state.ClarificationIncomplete = false;

        _logger?.LogInformation(
            "Session {SessionId}: CLARIFICATION complete → starting full debate chain", state.SessionId);

        await _sessionService.AdvancePhaseAsync(state, SessionPhase.AnalysisRound, cancellationToken);

        // Fire-and-forget: run the full debate chain in its own DI scope so the HTTP
        // request can return immediately while agents work in the background.
        var sessionId = state.SessionId;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Yield();
                using var scope = _scopeFactory.CreateScope();
                var scopedOrchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                var scopedSessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
                var scopedState = await scopedSessionService.GetAsync(sessionId, CancellationToken.None);

                if (scopedState is null)
                {
                    _logger?.LogWarning("Session {SessionId}: State not found for background debate chain", sessionId);
                    return;
                }

                // Copy over in-memory state that isn't persisted to the DB between calls
                scopedState.DebateBrief = brief;

                await scopedOrchestrator.RunFullDebateChainAsync(scopedState, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Session {SessionId}: Full debate chain failed", sessionId);
                await MarkSessionFailedAsync(sessionId, "background debate chain failure");
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Called when max clarification rounds are reached without a READY signal.
    /// Transitions to ANALYSIS_ROUND with clarification_incomplete flagged.
    /// </summary>
    public async Task SignalClarificationTimedOutAsync(
        SessionState state,
        DebateBrief partialBrief,
        CancellationToken cancellationToken)
    {
        state.DebateBrief = partialBrief;
        state.ClarificationIncomplete = true;

        _logger?.LogWarning(
            "Session {SessionId}: CLARIFICATION timed out — proceeding with partial brief",
            state.SessionId);

        await _sessionService.AdvancePhaseAsync(state, SessionPhase.AnalysisRound, cancellationToken);

        var sessionId = state.SessionId;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Yield();
                using var scope = _scopeFactory.CreateScope();
                var scopedOrchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                var scopedSessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
                var scopedState = await scopedSessionService.GetAsync(sessionId, CancellationToken.None);

                if (scopedState is null)
                {
                    _logger?.LogWarning("Session {SessionId}: State not found for background debate chain (timed out clarification)", sessionId);
                    return;
                }

                scopedState.DebateBrief = partialBrief;
                await scopedOrchestrator.RunFullDebateChainAsync(scopedState, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Session {SessionId}: Full debate chain failed (timed out clarification)", sessionId);
                await MarkSessionFailedAsync(sessionId, "background debate chain failure after timed out clarification");
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Runs the complete debate chain: Analysis → Critique → Synthesis → (Targeted loops) → Deliver.
    /// Called as a single unit from the background task so that every phase flows into the next.
    /// </summary>
    public async Task RunFullDebateChainAsync(SessionState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Session {SessionId}: Starting full debate chain (Analysis → Critique → Synthesis)",
            state.SessionId);

        try
        {
            await RunAnalysisRoundAsync(state, cancellationToken);
            // Each phase method below continues the chain automatically.
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Session {SessionId}: Debate chain failed unexpectedly. Terminating with gaps.",
                state.SessionId);

            await TerminateWithGapsAsync(
                state,
                "unexpected error during debate chain",
                cancellationToken);
        }
    }

    /// <summary>
    /// Runs the Analysis Round:
    /// 1. Dispatches all council agents in parallel.
    /// 2. Applies patches, runs decay engine.
    /// 3. Records snapshot.
    /// 4. Transitions to CRITIQUE and continues the chain.
    /// </summary>
    public async Task RunAnalysisRoundAsync(SessionState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Session {SessionId}: Running ANALYSIS_ROUND {Round}", state.SessionId, state.CurrentRound);

        var responses = await _agentRunner.RunAnalysisRoundAsync(state, cancellationToken);

        // Cache messages for the upcoming Critique phase.
        foreach (var r in responses.Where(r => !r.TimedOut && r.Message.Length > 0))
            state.LastRoundMessages[r.AgentId] = r.Message;

        RunDecayEngine(state);
        await _sessionService.RecordRoundSnapshotAsync(state, cancellationToken);

        // Check termination conditions before continuing
        if (_policy.IsBudgetExhausted(state.TokensUsed))
        {
            await TerminateWithGapsAsync(state, "budget exhausted after analysis", cancellationToken);
            return;
        }

        if (state.CurrentRound >= _policy.MaxDebateRounds && !IsConverged(state))
        {
            await TerminateWithGapsAsync(
                state, $"max debate rounds ({_policy.MaxDebateRounds}) reached without convergence",
                cancellationToken);
            return;
        }

        // Transition to Critique and immediately run it
        await _sessionService.AdvancePhaseAsync(state, SessionPhase.Critique, cancellationToken);
        await RunCritiqueRoundAsync(state, cancellationToken);
    }

    /// <summary>
    /// Runs the Critique Round:
    /// 1. Dispatches all council agents in parallel with cross-agent critique targets.
    /// 2. Applies patches, runs decay engine.
    /// 3. Records snapshot.
    /// 4. Transitions to SYNTHESIS and immediately runs it.
    /// </summary>
    public async Task RunCritiqueRoundAsync(SessionState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Session {SessionId}: Running CRITIQUE Round {Round}", state.SessionId, state.CurrentRound);

        await _agentRunner.RunCritiqueRoundAsync(state, cancellationToken);

        RunDecayEngine(state);
        await _sessionService.RecordRoundSnapshotAsync(state, cancellationToken);

        if (_policy.IsBudgetExhausted(state.TokensUsed))
        {
            await TerminateWithGapsAsync(state, "budget exhausted after critique", cancellationToken);
            return;
        }

        // Transition to Synthesis and immediately run it
        await _sessionService.AdvancePhaseAsync(state, SessionPhase.Synthesis, cancellationToken);
        await RunSynthesisAsync(state, cancellationToken);
    }

    /// <summary>
    /// Runs the Synthesis pass:
    /// 1. Dispatches the Synthesizer.
    /// 2. Applies convergence scores from the patch.
    /// 3. Decides the next phase based on convergence outcome.
    /// </summary>
    public async Task RunSynthesisAsync(SessionState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Session {SessionId}: Running SYNTHESIS", state.SessionId);

        await _agentRunner.RunSynthesisAsync(state, cancellationToken);

        if (_policy.IsBudgetExhausted(state.TokensUsed))
        {
            await TerminateWithGapsAsync(state, "budget exhausted after synthesis", cancellationToken);
            return;
        }

        var convergence = state.TruthMap.Convergence;
        var converged = _policy.ShouldConverge(
            convergence.Overall,
            convergence.AssumptionExplicitness,
            convergence.EvidenceQuality,
            state.TruthMap.HasBlockingOpenQuestions(),
            state.FrictionLevel);

        if (converged)
        {
            _logger?.LogInformation(
                "Session {SessionId}: Converged (score={Score:F2}) → DELIVER",
                state.SessionId, convergence.Overall);

            state.Status = SessionStatus.Complete;
            await _sessionService.AdvancePhaseAsync(state, SessionPhase.Deliver, cancellationToken);
            // Deliver is terminal — no further chain continuation needed.
        }
        else if (state.TargetedLoopCount < _policy.MaxTargetedLoops)
        {
            _logger?.LogInformation(
                "Session {SessionId}: Not converged (score={Score:F2}, loops={Count}/{Max}) → TARGETED_LOOP",
                state.SessionId, convergence.Overall, state.TargetedLoopCount, _policy.MaxTargetedLoops);

            await _sessionService.AdvancePhaseAsync(state, SessionPhase.TargetedLoop, cancellationToken);

            // Run a targeted loop: target all non-synthesizer agents for the gaps
            var targetAgentIds = new List<string>
            {
                Domain.Agents.AgentId.GptAgent,
                Domain.Agents.AgentId.GeminiAgent,
                Domain.Agents.AgentId.ClaudeAgent
            };

            var gapDirective = "Address the remaining convergence gaps identified in the previous synthesis. " +
                               "Focus only on open questions and unresolved claims. Do not introduce new major topics.";

            await RunTargetedLoopAsync(state, targetAgentIds, gapDirective, cancellationToken);
        }
        else
        {
            await TerminateWithGapsAsync(
                state, $"max targeted loops ({_policy.MaxTargetedLoops}) exhausted", cancellationToken);
        }
    }

    /// <summary>
    /// Runs a Targeted Loop for specific agents addressing identified convergence gaps.
    /// After completion, re-enters SYNTHESIS.
    /// </summary>
    public async Task RunTargetedLoopAsync(
        SessionState state,
        IReadOnlyList<string> targetAgentIds,
        string microDirective,
        CancellationToken cancellationToken)
    {
        state.TargetedLoopCount++;
        state.CurrentRound++;

        _logger?.LogInformation(
            "Session {SessionId}: Running TARGETED_LOOP {Count} — targeting {Agents}",
            state.SessionId, state.TargetedLoopCount,
            string.Join(", ", targetAgentIds));

        await _agentRunner.RunTargetedLoopAsync(
            state, targetAgentIds, microDirective, cancellationToken);

        RunDecayEngine(state);
        await _sessionService.RecordRoundSnapshotAsync(state, cancellationToken);

        if (_policy.IsBudgetExhausted(state.TokensUsed))
        {
            await TerminateWithGapsAsync(
                state, "budget exhausted during targeted loop", cancellationToken);
            return;
        }

        await _sessionService.AdvancePhaseAsync(state, SessionPhase.Synthesis, cancellationToken);
        // Re-enter synthesis to evaluate convergence again after the targeted loop.
        await RunSynthesisAsync(state, cancellationToken);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void RunDecayEngine(SessionState state)
    {
        // Build activity from the contested claims in the current Truth Map.
        // The Orchestrator reads challenged/defended claim IDs from the Truth Map itself
        // (because patches are already applied at this point).
        var contested = state.TruthMap.GetContestedClaims();
        var activity = new RoundActivity(
            ChallengedClaimIds: contested.Select(c => c.Id).ToHashSet(),
            DefendedClaimIds: new HashSet<string>(),
            NewEvidenceIds: state.TruthMap.Evidence.Select(e => e.Id).ToHashSet());

        var (updatedMap, transitions) = _decayEngine.Apply(state.TruthMap, activity);
        state.TruthMap = updatedMap;

        _logger?.LogDebug(
            "Session {SessionId}: Decay engine applied — {Count} transitions",
            state.SessionId, transitions.Count);
    }

    private async Task TerminateWithGapsAsync(
        SessionState state,
        string reason,
        CancellationToken cancellationToken)
    {
        _logger?.LogWarning(
            "Session {SessionId}: Terminating with gaps — {Reason}", state.SessionId, reason);

        state.Status = SessionStatus.CompleteWithGaps;
        await _sessionService.AdvancePhaseAsync(state, SessionPhase.DeliverWithGaps, cancellationToken);
    }

    private bool IsConverged(SessionState state)
    {
        var c = state.TruthMap.Convergence;
        return _policy.ShouldConverge(
            c.Overall, c.AssumptionExplicitness, c.EvidenceQuality,
            state.TruthMap.HasBlockingOpenQuestions(), state.FrictionLevel);
    }

    private async Task MarkSessionFailedAsync(Guid sessionId, string reason)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scopedSessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var scopedState = await scopedSessionService.GetAsync(sessionId, CancellationToken.None);

            if (scopedState is null)
            {
                _logger?.LogWarning(
                    "Session {SessionId}: Unable to mark failed session (state not found)", sessionId);
                return;
            }

            if (scopedState.Status is SessionStatus.Complete or SessionStatus.CompleteWithGaps)
            {
                // Already terminal.
                return;
            }

            scopedState.Status = SessionStatus.CompleteWithGaps;
            await scopedSessionService.AdvancePhaseAsync(
                scopedState,
                SessionPhase.DeliverWithGaps,
                CancellationToken.None);
        }
        catch (Exception markEx)
        {
            _logger?.LogError(
                markEx,
                "Session {SessionId}: Failed to mark session as complete_with_gaps after background failure",
                sessionId);
        }
    }
}
