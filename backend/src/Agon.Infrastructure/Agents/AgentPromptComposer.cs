using System.Linq;
using System.Text.Json;
using Agon.Application.Orchestration;
using Agon.Domain.Agents;

namespace Agon.Infrastructure.Agents;

public static class AgentPromptComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ComposePrompt(string agentId, AgentContext context)
    {
        var normalizedAgentId = NormalizeAgentId(agentId);
        var systemPrompt = AgentSystemPrompts.GetPrompt(normalizedAgentId);
        var directives = context.MicroDirectives
            .Where(directive => !string.IsNullOrWhiteSpace(directive))
            .Select(directive => directive.Trim())
            .ToList();

        var directivesBlock = directives.Count == 0
            ? string.Empty
            : $"ADDITIONAL DIRECTIVES:\n- {string.Join("\n- ", directives)}";

        var truthMapJson = JsonSerializer.Serialize(context.TruthMap, JsonOptions);

        return $"""
            {systemPrompt}

            SESSION CONTEXT:
            SessionId: {context.SessionId}
            Round: {context.Round}
            Phase: {context.Phase}
            FrictionLevel: {context.FrictionLevel}

            TRUTH MAP JSON:
            {truthMapJson}

            {directivesBlock}
            """;
    }

    private static string NormalizeAgentId(string agentId) =>
        (agentId ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
}
