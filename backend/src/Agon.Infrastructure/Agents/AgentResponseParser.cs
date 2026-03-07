using System.Text.Json;
using System.Text.RegularExpressions;
using Agon.Application.Models;
using Agon.Domain.TruthMap;

namespace Agon.Infrastructure.Agents;

/// <summary>
/// Parses raw LLM output into MESSAGE + PATCH sections.
/// Expected format:
/// <code>
/// ## MESSAGE
/// Human-readable text...
///
/// ## PATCH
/// ```json
/// { "ops": [...], "meta": {...} }
/// ```
/// </code>
/// If PATCH section is missing or malformed, returns null for Patch.
/// </summary>
public static class AgentResponseParser
{
    private static readonly Regex MessageHeaderRegex = new(
        @"^##\s*MESSAGE\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PatchHeaderRegex = new(
        @"^##\s*PATCH\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsonCodeBlockRegex = new(
        @"```json\s*(.*?)\s*```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses raw agent output into an AgentResponse with MESSAGE, PATCH, and token estimate.
    /// </summary>
    public static AgentResponse Parse(string rawOutput, string agentId)
    {
        var message = ExtractMessage(rawOutput);
        var patch = ExtractPatch(rawOutput);
        var tokensUsed = EstimateTokens(rawOutput);

        return new AgentResponse(
            AgentId: agentId,
            Message: message,
            Patch: patch,
            TokensUsed: tokensUsed,
            TimedOut: false,
            RawOutput: rawOutput);
    }

    // ── MESSAGE extraction ────────────────────────────────────────────────────

    private static string ExtractMessage(string raw)
    {
        var messageMatch = MessageHeaderRegex.Match(raw);
        if (!messageMatch.Success)
        {
            // No MESSAGE header found — treat entire text as message if no PATCH header either
            var patchHeaderMatch = PatchHeaderRegex.Match(raw);
            return patchHeaderMatch.Success ? string.Empty : raw.Trim();
        }

        var messageStart = messageMatch.Index + messageMatch.Length;
        var patchHeaderMatch2 = PatchHeaderRegex.Match(raw, messageStart);

        var messageEnd = patchHeaderMatch2.Success ? patchHeaderMatch2.Index : raw.Length;
        var messageText = raw[messageStart..messageEnd].Trim();

        return messageText;
    }

    // ── PATCH extraction ──────────────────────────────────────────────────────

    private static TruthMapPatch? ExtractPatch(string raw)
    {
        var patchMatch = PatchHeaderRegex.Match(raw);
        if (!patchMatch.Success) return null;

        var patchSectionStart = patchMatch.Index + patchMatch.Length;
        var patchSection = raw[patchSectionStart..];

        var jsonMatch = JsonCodeBlockRegex.Match(patchSection);
        if (!jsonMatch.Success) return null;

        var json = jsonMatch.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var patch = JsonSerializer.Deserialize<TruthMapPatch>(json, options);
            return patch;
        }
        catch (JsonException)
        {
            // Malformed JSON — return null, log will happen in caller
            return null;
        }
    }

    // ── Token estimation ──────────────────────────────────────────────────────

    /// <summary>
    /// Rough token estimation: ~1.3 tokens per word. Overestimates are safer for budget tracking.
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return (int)(words.Length * 1.3);
    }
}
