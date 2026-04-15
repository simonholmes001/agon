using Agon.Application.Models;
using System.Text.RegularExpressions;

namespace Agon.Application.Orchestration;

internal static partial class PostDeliveryCouncilClassifier
{
    [GeneratedRegex(@"\b(invoke(?:\s+the)?\s+council|run(?:\s+the)?\s+council|agent\s+council|multi-agent|full\s+debate|all\s+agents)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CouncilTriggerRegex();

    [GeneratedRegex(@"\b(summari[sz]e|improv(?:e|ement)|revise|rewrite|analy[sz]e|review|assess|evaluate|compare|synthesi[sz]e|expand|deep\s+dive|trade[- ]?off|tradeoff|recommend(?:ation)?|critique|risk(?:s)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ComplexIntentRegex();

    [GeneratedRegex(@"\b(attachment|attached|document|file|pdf|image|photo|screenshot)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AttachmentContextRegex();

    [GeneratedRegex(@"\b[\w'-]+\b")]
    private static partial Regex WordRegex();

    public static PostDeliveryCouncilDecision Classify(SessionState state, string latestUserMessage)
    {
        if (string.IsNullOrWhiteSpace(latestUserMessage))
        {
            return PostDeliveryCouncilDecision.None;
        }

        var trimmed = latestUserMessage.Trim();
        if (trimmed.Length < 4)
        {
            return PostDeliveryCouncilDecision.None;
        }

        if (CouncilTriggerRegex().IsMatch(trimmed))
        {
            return PostDeliveryCouncilDecision.Invoke;
        }

        if (!ComplexIntentRegex().IsMatch(trimmed))
        {
            return PostDeliveryCouncilDecision.None;
        }

        var wordCount = WordRegex().Matches(trimmed).Count;
        if (wordCount < 4)
        {
            return PostDeliveryCouncilDecision.None;
        }

        return state.Attachments.Count > 0 || AttachmentContextRegex().IsMatch(trimmed)
            ? PostDeliveryCouncilDecision.Propose
            : PostDeliveryCouncilDecision.None;
    }

    public static bool ShouldProposeCouncil(SessionState state, string latestUserMessage) =>
        Classify(state, latestUserMessage) == PostDeliveryCouncilDecision.Propose;

    public static bool ShouldInvokeCouncil(SessionState state, string latestUserMessage) =>
        Classify(state, latestUserMessage) == PostDeliveryCouncilDecision.Invoke;
}

internal enum PostDeliveryCouncilDecision
{
    None = 0,
    Propose = 1,
    Invoke = 2
}
