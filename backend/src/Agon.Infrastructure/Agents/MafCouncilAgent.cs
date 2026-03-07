using Agon.Application.Interfaces;
using Microsoft.Agents.AI;
using System.Text;
using System.Text.Json;
using AgonAgentResponse = Agon.Application.Models.AgentResponse;
using AgonAgentContext = Agon.Application.Models.AgentContext;
using TruthMapModel = Agon.Domain.TruthMap.TruthMap;

namespace Agon.Infrastructure.Agents;

/// <summary>
/// Adapter that wraps MAF's native AIAgent and provides our application-specific
/// context building (AgentContext → prompt) and response parsing (raw text → AgentResponse).
/// </summary>
public sealed class MafCouncilAgent : ICouncilAgent
{
    private readonly IAgentResponseParser _parser;

    public string AgentId { get; }
    public string ModelProvider { get; }
    public AIAgent UnderlyingAgent { get; }

    public MafCouncilAgent(
        string agentId,
        string modelProvider,
        AIAgent underlyingAgent,
        IAgentResponseParser parser)
    {
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        ModelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        UnderlyingAgent = underlyingAgent ?? throw new ArgumentNullException(nameof(underlyingAgent));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<AgonAgentResponse> RunAsync(AgonAgentContext context, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = BuildPrompt(context);
            // MAF AIAgent.RunAsync signature: RunAsync(string message, AgentSession? session, AgentRunOptions? options, CancellationToken ct)
            var result = await UnderlyingAgent.RunAsync(prompt, session: null, options: null, cancellationToken);

            var rawResponse = result.ToString() ?? string.Empty;
            return _parser.Parse(rawResponse, AgentId);
        }
        catch (OperationCanceledException)
        {
            return AgonAgentResponse.CreateTimedOut(AgentId);
        }
        catch (Exception ex)
        {
            // Log exception in production
            return new AgonAgentResponse(
                AgentId: AgentId,
                Message: $"[ERROR] Agent failed: {ex.Message}",
                Patch: null,
                TokensUsed: 0,
                TimedOut: false,
                RawOutput: null);
        }
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(
        AgonAgentContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(context);
        
        // MAF AIAgent.RunStreamingAsync returns IAsyncEnumerable<AgentResponseUpdate>
        await foreach (var update in UnderlyingAgent.RunStreamingAsync(prompt, session: null, options: null, cancellationToken))
        {
            // AgentResponseUpdate has .Text property and .ToString() method
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private string BuildPrompt(AgonAgentContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Session Context");
        sb.AppendLine($"Session ID: {context.SessionId}");
        sb.AppendLine($"Round: {context.RoundNumber}");
        sb.AppendLine($"Phase: {context.Phase}");
        sb.AppendLine($"Friction Level: {context.FrictionLevel}");
        sb.AppendLine();

        sb.AppendLine("# Current Truth Map");
        sb.AppendLine("```json");
        sb.AppendLine(SerializeTruthMap(context.TruthMap));
        sb.AppendLine("```");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(context.MicroDirective))
        {
            sb.AppendLine("# Task Directive");
            sb.AppendLine(context.MicroDirective);
            sb.AppendLine();
        }

        if (context.CritiqueTargetMessages.Count > 0)
        {
            sb.AppendLine("# Critique Targets");
            sb.AppendLine("You are assigned to critique the following agent responses:");
            foreach (var targetMessage in context.CritiqueTargetMessages)
            {
                sb.AppendLine($"## {targetMessage.AgentId}");
                sb.AppendLine(targetMessage.Message);
                sb.AppendLine();
            }
        }

        sb.AppendLine("# Required Output Format");
        sb.AppendLine("Your response MUST include both sections:");
        sb.AppendLine();
        sb.AppendLine("## MESSAGE");
        sb.AppendLine("Your analysis in Markdown format.");
        sb.AppendLine();
        sb.AppendLine("## PATCH");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"ops\": [");
        sb.AppendLine("    {\"op\": \"add\", \"path\": \"/claims/-\", \"value\": {...}}");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"meta\": {");
        sb.AppendLine($"    \"agent\": \"{AgentId}\",");
        sb.AppendLine($"    \"round\": {context.RoundNumber},");
        sb.AppendLine($"    \"reason\": \"Brief explanation\",");
        sb.AppendLine($"    \"session_id\": \"{context.SessionId}\"");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string SerializeTruthMap(TruthMapModel map)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            return JsonSerializer.Serialize(map, options);
        }
        catch
        {
            return "{}";
        }
    }
}
