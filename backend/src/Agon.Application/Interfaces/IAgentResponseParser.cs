using Agon.Application.Models;

namespace Agon.Application.Interfaces;

/// <summary>
/// Parses raw LLM output into structured AgentResponse with MESSAGE and PATCH sections.
/// </summary>
public interface IAgentResponseParser
{
    /// <summary>
    /// Extracts MESSAGE (Markdown) and PATCH (JSON) sections from raw agent output.
    /// </summary>
    /// <param name="rawOutput">The raw text output from the LLM.</param>
    /// <param name="agentId">The agent that produced the output (for error handling).</param>
    /// <returns>Structured AgentResponse with parsed MESSAGE and PATCH.</returns>
    AgentResponse Parse(string rawOutput, string agentId);
}
