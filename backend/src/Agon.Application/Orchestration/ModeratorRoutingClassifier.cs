using Agon.Application.Models;
using System.Text.RegularExpressions;

namespace Agon.Application.Orchestration;

internal static class ModeratorRoutingClassifier
{
    private static readonly Regex FullDebateIntentRegex = new(
        @"\b(prd|product requirements?|debate brief|architecture|roadmap|mvp|spec(?:ification)?|tech stack|user stor(?:y|ies)|risk(?:s)?|assumption(?:s)?|implementation(?: plan)?|migration|refactor|go[- ]?to[- ]?market)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SimpleMetaQueryRegex = new(
        @"\b(what can you do|how can you help|who are you|what is agon|internal setup|your setup|how do you work|capabilities|command(?:s)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SelfReferenceRegex = new(
        @"\b(agon|you|your|this assistant|this tool|this cli)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SystemMetaTopicRegex = new(
        @"\b(agent(?:s)?|llm(?:s)?|model(?:s)?|internal|setup|architecture|capabilit(?:y|ies)|command(?:s)?|work(?:s|ing)?|help)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex QuestionLeadRegex = new(
        @"^\s*(how|what|who|can|could|would|do|does|is|are|where|when)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UtilityImperativeLeadRegex = new(
        @"^\s*(give|show|tell|explain|describe|define|check|summari[sz]e|compare|help\s+me\s+understand|today(?:'s)?|current)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UtilityTopicRegex = new(
        @"\b(weather|forecast|temperature|rain|wind|postcode|dns|domain|record(?:\s+types?)?|a/aaaa|cname|mx|txt|http|https|ssl|tls|certificate|hostname|ip(?:v4|v6)?|status\s*code|timezone|utc)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"\b[\w'-]+\b", RegexOptions.Compiled);
    private static readonly Regex AttachmentDirectActionRegex = new(
        @"\b(describe|summari[sz]e|caption|transcribe|read|extract|analy[sz]e)\b.*\b(image|photo|picture|screenshot|scan|document|file|pdf|attachment)\b|\b(this|attached)\s+(image|document|file|pdf|attachment)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ShouldRunIntentRouter(SessionState state)
    {
        var latestInput = GetLatestUserInput(state);
        if (string.IsNullOrWhiteSpace(latestInput))
        {
            return false;
        }

        var trimmed = latestInput.Trim();
        if (trimmed.Length is < 4 or > 600)
        {
            return false;
        }

        if (ShouldForceDirectAnswer(state))
        {
            return false;
        }

        return LooksLikeQuestion(trimmed);
    }

    public static bool ShouldForceDirectAnswer(SessionState state)
    {
        var latestInput = GetLatestUserInput(state);
        if (string.IsNullOrWhiteSpace(latestInput))
        {
            return false;
        }

        return LooksLikeSimpleDirectQuery(latestInput, state.Attachments.Count > 0);
    }

    private static string GetLatestUserInput(SessionState state)
    {
        if (state.UserMessages.Count > 0)
        {
            return state.UserMessages[^1].Content;
        }

        return state.Idea ?? string.Empty;
    }

    private static bool LooksLikeSimpleDirectQuery(string input, bool hasAttachments)
    {
        var trimmed = input.Trim();
        if (trimmed.Length is < 4 or > 280)
        {
            return false;
        }

        var attachmentImperative = hasAttachments && AttachmentDirectActionRegex.IsMatch(trimmed);
        var utilityImperative = LooksLikeUtilityImperative(trimmed);
        if (!LooksLikeQuestion(trimmed) && !attachmentImperative && !utilityImperative)
        {
            return false;
        }

        var wordCount = WordRegex.Matches(trimmed).Count;
        if (wordCount > 45)
        {
            return false;
        }

        if (SimpleMetaQueryRegex.IsMatch(trimmed))
        {
            return true;
        }

        var selfReferential = SelfReferenceRegex.IsMatch(trimmed);
        var systemTopic = SystemMetaTopicRegex.IsMatch(trimmed);
        if (selfReferential && systemTopic)
        {
            return true;
        }

        return !FullDebateIntentRegex.IsMatch(trimmed);
    }

    private static bool LooksLikeQuestion(string input)
    {
        return input.Contains('?') || QuestionLeadRegex.IsMatch(input);
    }

    private static bool LooksLikeUtilityImperative(string input)
    {
        return UtilityImperativeLeadRegex.IsMatch(input) && UtilityTopicRegex.IsMatch(input);
    }
}
