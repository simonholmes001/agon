using Agon.Application.Models;
using Agon.Application.Services;
using Agon.Domain.Agents;
using Agon.Domain.Engines;
using Agon.Domain.Sessions;
using Microsoft.Extensions.Logging;

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
    private readonly IAgentRunner _agentRunner;
    private readonly ISessionService _sessionService;
    private readonly RoundPolicy _policy;
    private readonly ConfidenceDecayEngine _decayEngine;
    private readonly ILogger<Orchestrator>? _logger;

    public Orchestrator(
        IAgentRunner agentRunner,
        ISessionService sessionService,
        RoundPolicy policy,
        ILogger<Orchestrator>? logger = null)
    {
        _agentRunner = agentRunner;
        _sessionService = sessionService;
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

        // Check if Moderator asked questions (heuristic: contains "?" in the message)
        var askedQuestions = response.Message.Contains('?');

        // Check if Moderator signaled READY
        var signaledReady = response.Message.Contains("READY", StringComparison.OrdinalIgnoreCase);

        // CRITICAL RULE: If Moderator asked questions, IGNORE the READY signal
        // The LLM sometimes outputs both - we enforce the rule here
        if (signaledReady && !askedQuestions)
        {
            _logger?.LogInformation(
                "Session {SessionId}: Moderator signaled READY → transitioning to ANALYSIS_ROUND",
                state.SessionId);

            // Extract Debate Brief from the Truth Map (updated by agent runner)
            var brief = ExtractDebateBrief(state.TruthMap);
            await SignalClarificationCompleteAsync(state, brief, cancellationToken);
        }
        else
        {
            if (signaledReady && askedQuestions)
            {
                _logger?.LogWarning(
                    "Session {SessionId}: Moderator signaled READY but also asked questions - ignoring READY signal",
                    state.SessionId);
            }

            // Moderator asked questions - increment clarification round count
            state.ClarificationRoundCount++;
            await _sessionService.RecordRoundSnapshotAsync(state, cancellationToken);

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

    /// <summary>
    /// Called when the Moderator signals READY at the end of the Clarification phase.
    /// Stores the completed Debate Brief and transitions to ANALYSIS_ROUND.
    /// </summary>
    public async Task SignalClarificationCompleteAsync(
        SessionState state,
        DebateBrief brief,
        CancellationToken cancellationToken)
    {
        state.DebateBrief = brief;
        state.ClarificationIncomplete = false;

        _logger?.LogInformation(
            "Session {SessionId}: CLARIFICATION complete → ANALYSIS_ROUND", state.SessionId);

        await _sessionService.AdvancePhaseAsync(state, SessionPhase.AnalysisRound, cancellationToken);
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
    }

    /// <summary>
    /// Runs the Analysis Round:
    /// 1. Dispatches all council agents in parallel.
    /// 2. Applies patches, runs decay engine.
    /// 3. Records snapshot.
    /// 4. Transitions to CRITIQUE (or terminates early if budget exhausted or max rounds hit).
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

        await TransitionAfterRoundAsync(state, responses, isDebateRound: true, cancellationToken);
    }

    /// <summary>
    /// Runs the Critique Round:
    /// 1. Dispatches all council agents in parallel with cross-agent critique targets.
    /// 2. Applies patches, runs decay engine.
    /// 3. Records snapshot.
    /// 4. Transitions to SYNTHESIS.
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

        await _sessionService.AdvancePhaseAsync(state, SessionPhase.Synthesis, cancellationToken);
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
        }
        else if (state.TargetedLoopCount < _policy.MaxTargetedLoops)
        {
            _logger?.LogInformation(
                "Session {SessionId}: Not converged (score={Score:F2}, loops={Count}/{Max}) → TARGETED_LOOP",
                state.SessionId, convergence.Overall, state.TargetedLoopCount, _policy.MaxTargetedLoops);

            await _sessionService.AdvancePhaseAsync(state, SessionPhase.TargetedLoop, cancellationToken);
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
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Determines next phase after a debate round (Analysis).
    /// Handles: budget exhaustion, max rounds, or normal → CRITIQUE transition.
    /// </summary>
    private async Task TransitionAfterRoundAsync(
        SessionState state,
        IReadOnlyList<AgentResponse> responses,
        bool isDebateRound,
        CancellationToken cancellationToken)
    {
        _ = responses; // reserved for future extension (e.g., checking timed-out ratio)

        if (_policy.IsBudgetExhausted(state.TokensUsed))
        {
            await TerminateWithGapsAsync(state, "budget exhausted", cancellationToken);
            return;
        }

        if (isDebateRound && state.CurrentRound >= _policy.MaxDebateRounds
            && !IsConverged(state))
        {
            await TerminateWithGapsAsync(
                state, $"max debate rounds ({_policy.MaxDebateRounds}) reached without convergence",
                cancellationToken);
            return;
        }

        await _sessionService.AdvancePhaseAsync(state, SessionPhase.Critique, cancellationToken);
    }

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
}
