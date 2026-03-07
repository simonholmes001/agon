using Agon.Application.Interfaces;
using Agon.Application.Models;

namespace Agon.Infrastructure.Agents;

/// <summary>
/// Adapter that implements IAgentResponseParser by delegating to the static AgentResponseParser class.
/// </summary>
public sealed class AgentResponseParserAdapter : IAgentResponseParser
{
    public AgentResponse Parse(string rawOutput, string agentId)
    {
        return AgentResponseParser.Parse(rawOutput, agentId);
    }
}
