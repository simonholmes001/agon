using Agon.Domain.TruthMap;
using Agon.Infrastructure.Agents;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Agents;

public class AgentResponseParserTests
{
    // ── Valid MESSAGE + PATCH format ──────────────────────────────────────────

    [Fact]
    public void Parse_ValidMessageAndPatch_ReturnsPopulatedResponse()
    {
        var raw = """
            ## MESSAGE
            This is the agent's human-readable analysis.
            It can span multiple lines.

            ## PATCH
            ```json
            {
              "ops": [],
              "meta": {
                "agent": "gpt_agent",
                "round": 1,
                "reason": "Initial analysis",
                "session_id": "00000000-0000-0000-0000-000000000000"
              }
            }
            ```
            """;

        var result = AgentResponseParser.Parse(raw, "gpt_agent");

        result.Message.Should().Contain("agent's human-readable analysis");
        result.Patch.Should().NotBeNull();
        result.Patch!.Meta.Agent.Should().Be("gpt_agent");
        result.RawOutput.Should().Be(raw);
    }

    [Fact]
    public void Parse_MessageOnly_NoPatch()
    {
        var raw = """
            ## MESSAGE
            Just a message with no patch section.
            """;

        var result = AgentResponseParser.Parse(raw, "gpt_agent");

        result.Message.Should().Contain("Just a message");
        result.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_PatchWithMultipleOperations_DeserializesCorrectly()
    {
        var raw = """
            ## MESSAGE
            Analysis complete.

            ## PATCH
            ```json
            {
              "ops": [
                {
                  "op": "add",
                  "path": "/claims/-",
                  "value": null
                }
              ],
              "meta": {
                "agent": "gpt_agent",
                "round": 1,
                "reason": "Test",
                "session_id": "00000000-0000-0000-0000-000000000000"
              }
            }
            ```
            """;

        var result = AgentResponseParser.Parse(raw, "gpt_agent");

        result.Patch.Should().NotBeNull();
        result.Patch!.Ops.Should().HaveCount(1);
        result.Patch.Ops[0].Op.Should().Be(PatchOp.Add);
        result.Patch.Ops[0].Path.Should().Be("/claims/-");
    }

    // ── Malformed input ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_MalformedJson_ReturnsNullPatch()
    {
        var raw = """
            ## MESSAGE
            Analysis.

            ## PATCH
            ```json
            { this is not valid json }
            ```
            """;

        var result = AgentResponseParser.Parse(raw, "gpt_agent");

        result.Message.Should().Contain("Analysis");
        result.Patch.Should().BeNull(); // Parser gracefully handles bad JSON
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyMessage()
    {
        var result = AgentResponseParser.Parse("", "gpt_agent");

        result.Message.Should().BeEmpty();
        result.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_NoSectionHeaders_TreatsEntireTextAsMessage()
    {
        var raw = "Just plain text with no headers.";

        var result = AgentResponseParser.Parse(raw, "gpt_agent");

        result.Message.Should().Be(raw);
        result.Patch.Should().BeNull();
    }

    // ── Case insensitivity ────────────────────────────────────────────────────

    [Fact]
    public void Parse_SectionHeadersCaseInsensitive()
    {
        var raw = """
            ## message
            Content.

            ## patch
            ```json
            {
              "ops": [],
              "meta": {
                "agent": "gpt_agent",
                "round": 1,
                "reason": "Test",
                "session_id": "00000000-0000-0000-0000-000000000000"
              }
            }
            ```
            """;

        var result = AgentResponseParser.Parse(raw, "gpt_agent");

        result.Message.Should().Contain("Content");
        result.Patch.Should().NotBeNull();
    }

    // ── Token count estimation ────────────────────────────────────────────────

    [Fact]
    public void Parse_EstimatesTokenCount()
    {
        var raw = """
            ## MESSAGE
            This is a message with approximately twenty words in it so we can test token estimation properly indeed yes.

            ## PATCH
            ```json
            { "ops": [], "meta": { "agent": "gpt_agent", "round": 1, "reason": "Test", "session_id": "00000000-0000-0000-0000-000000000000" } }
            ```
            """;

        var result = AgentResponseParser.Parse(raw, "gpt_agent");

        // Rough heuristic: ~1.3 tokens per word, so ~20 words ≈ 26 tokens for message
        // Plus JSON overhead for patch. Should be > 0 and reasonable.
        result.TokensUsed.Should().BeGreaterThan(20);
        result.TokensUsed.Should().BeLessThan(200); // sanity check
    }
}
