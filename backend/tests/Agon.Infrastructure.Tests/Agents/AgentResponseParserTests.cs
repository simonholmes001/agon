using Agon.Infrastructure.Agents;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Agents;

public class AgentResponseParserTests
{
    [Fact]
    public void Parse_ExtractsMessageAndPatch_WhenBothSectionsArePresent()
    {
        var sessionId = Guid.NewGuid();
        var raw = $$"""
            ## MESSAGE
            This is the user-visible summary.

            ## PATCH
            {
              "ops": [],
              "meta": {
                "agent": "synthesis_validation",
                "round": 1,
                "reason": "test",
                "sessionId": "{{sessionId}}"
              }
            }
            """;

        var parsed = AgentResponseParser.Parse(raw);

        parsed.Message.Should().Be("This is the user-visible summary.");
        parsed.Patch.Should().NotBeNull();
        parsed.Patch!.Ops.Should().BeEmpty();
        parsed.Patch.Meta.Agent.Should().Be("synthesis_validation");
    }

    [Fact]
    public void Parse_ReturnsMessageAndIgnoresPatch_WhenPatchIsNotPatchSchema()
    {
        var raw = """
            ## MESSAGE
            Keep this message.

            ## PATCH
            { "decisions": [ { "id": "d-1" } ] }
            """;

        var parsed = AgentResponseParser.Parse(raw);

        parsed.Message.Should().Be("Keep this message.");
        parsed.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_ReturnsOriginalText_WhenMessageHeaderIsMissing()
    {
        var raw = "Plain response without structured sections.";

        var parsed = AgentResponseParser.Parse(raw);

        parsed.Message.Should().Be(raw);
        parsed.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_HandlesJsonCodeFenceInPatchSection()
    {
        var sessionId = Guid.NewGuid();
        var raw = $$"""
            ## MESSAGE
            A structured answer.

            ## PATCH
            ```json
            {
              "ops": [],
              "meta": {
                "agent": "synthesis_validation",
                "round": 2,
                "reason": "fenced",
                "sessionId": "{{sessionId}}"
              }
            }
            ```
            """;

        var parsed = AgentResponseParser.Parse(raw);

        parsed.Message.Should().Be("A structured answer.");
        parsed.Patch.Should().NotBeNull();
        parsed.Patch!.Meta.Round.Should().Be(2);
    }
}
