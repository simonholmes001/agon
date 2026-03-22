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
    private static readonly Regex WordRegex = new(@"\b[\w'-]+\b", RegexOptions.Compiled);

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

        return LooksLikeSimpleDirectQuery(latestInput);
    }

    private static string GetLatestUserInput(SessionState state)
    {
        if (state.UserMessages.Count > 0)
        {
            return state.UserMessages[^1].Content;
        }

        return state.Idea ?? string.Empty;
    }

    private static bool LooksLikeSimpleDirectQuery(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length is < 4 or > 280)
        {
            return false;
        }

        if (!LooksLikeQuestion(trimmed))
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
}
