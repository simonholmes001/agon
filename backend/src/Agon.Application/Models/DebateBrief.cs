namespace Agon.Application.Models;

/// <summary>
/// Output of the Socratic Clarifier. Seeds the initial Truth Map.
/// Produced once per session during CLARIFICATION, then treated as read-only.
/// </summary>
public sealed record DebateBrief(
    string CoreIdea,
    BriefConstraints Constraints,
    IReadOnlyList<string> SuccessMetrics,
    string PrimaryPersona,
    IReadOnlyList<string> OpenQuestions)
{
    public bool IsComplete() =>
        !string.IsNullOrWhiteSpace(CoreIdea)
        && SuccessMetrics.Count > 0
        && !string.IsNullOrWhiteSpace(PrimaryPersona);
}

/// <summary>Constraint values extracted during clarification.</summary>
public sealed record BriefConstraints(
    string Budget,
    string Timeline,
    IReadOnlyList<string> TechStack,
    IReadOnlyList<string> NonNegotiables);
