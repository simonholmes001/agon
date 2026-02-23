using System.Text.Json;
using System.Text.RegularExpressions;
using Agon.Domain.TruthMap;

namespace Agon.Infrastructure.Agents;

public static class AgentResponseParser
{
    private static readonly Regex MessageSectionRegex = new(
        @"^\s*#{1,6}\s*MESSAGE\s*$\s*(?<message>[\s\S]*?)(?=^\s*#{1,6}\s*PATCH\s*$|\z)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PatchSectionRegex = new(
        @"^\s*#{1,6}\s*PATCH\s*$\s*(?<patch>[\s\S]*)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static ParsedAgentResponse Parse(string? rawMessage)
    {
        var normalized = (rawMessage ?? string.Empty).Replace("\r", string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return new ParsedAgentResponse("Model returned an empty response.", null);
        }

        var message = ExtractMessage(normalized);
        var patch = ExtractPatch(normalized);
        return new ParsedAgentResponse(message, patch);
    }

    private static string ExtractMessage(string normalized)
    {
        var messageMatch = MessageSectionRegex.Match(normalized);
        if (!messageMatch.Success)
        {
            return normalized;
        }

        var message = messageMatch.Groups["message"].Value.Trim();
        return string.IsNullOrWhiteSpace(message)
            ? "Model returned an empty response."
            : message;
    }

    private static TruthMapPatch? ExtractPatch(string normalized)
    {
        var patchMatch = PatchSectionRegex.Match(normalized);
        if (!patchMatch.Success)
        {
            return null;
        }

        var patchText = patchMatch.Groups["patch"].Value.Trim();
        if (patchText.Length == 0)
        {
            return null;
        }

        patchText = UnwrapCodeFence(patchText);

        try
        {
            var patch = JsonSerializer.Deserialize<TruthMapPatch>(patchText, JsonOptions);
            if (patch?.Meta is null)
            {
                return null;
            }

            return patch;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string UnwrapCodeFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var lines = value.Split('\n').Select(line => line.TrimEnd()).ToList();
        if (lines.Count < 2)
        {
            return value;
        }

        if (!lines[^1].Trim().Equals("```", StringComparison.Ordinal))
        {
            return value;
        }

        return string.Join('\n', lines.Skip(1).Take(lines.Count - 2)).Trim();
    }
}

public sealed record ParsedAgentResponse(string Message, TruthMapPatch? Patch);
