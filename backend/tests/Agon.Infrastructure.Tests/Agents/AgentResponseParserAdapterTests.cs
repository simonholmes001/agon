using Agon.Application.Interfaces;
using Agon.Infrastructure.Agents;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Agents;

public class AgentResponseParserAdapterTests
{
    private readonly IAgentResponseParser _parser;

    public AgentResponseParserAdapterTests()
    {
        _parser = new AgentResponseParserAdapter();
    }

    [Fact]
    public void Parse_WithMessageAndPatch_ParsesBoth()
    {
        // Arrange
        var raw = @"## MESSAGE
This is the agent's message explaining the analysis.

## PATCH
```json
{
  ""ops"": [],
  ""meta"": {
    ""agent"": ""test-agent"",
    ""round"": 1,
    ""reason"": ""initial analysis""
  }
}
```";

        // Act
        var result = _parser.Parse(raw, "test-agent");

        // Assert
        result.AgentId.Should().Be("test-agent");
        result.Message.Should().Contain("This is the agent's message");
        result.Patch.Should().NotBeNull();
        result.Patch!.Meta.Agent.Should().Be("test-agent");
        result.Patch.Meta.Round.Should().Be(1);
        result.TimedOut.Should().BeFalse();
        result.RawOutput.Should().Be(raw);
    }

    [Fact]
    public void Parse_WithMessageOnly_ReturnsNullPatch()
    {
        // Arrange
        var raw = @"## MESSAGE
This is just a message without a patch.
The agent is providing clarification.";

        // Act
        var result = _parser.Parse(raw, "clarifier");

        // Assert
        result.AgentId.Should().Be("clarifier");
        result.Message.Should().Contain("This is just a message");
        result.Patch.Should().BeNull();
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public void Parse_WithNoHeaders_TreatsEntireTextAsMessage()
    {
        // Arrange
        var raw = "This is a simple message without any headers.";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.AgentId.Should().Be("agent");
        result.Message.Should().Be("This is a simple message without any headers.");
        result.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_WithMalformedJson_ReturnsNullPatch()
    {
        // Arrange
        var raw = @"## MESSAGE
Analysis complete.

## PATCH
```json
{ invalid json here }
```";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.AgentId.Should().Be("agent");
        result.Message.Should().Contain("Analysis complete");
        result.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_WithEmptyJsonBlock_ReturnsNullPatch()
    {
        // Arrange
        var raw = @"## MESSAGE
Message here.

## PATCH
```json
```";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.Message.Should().Contain("Message here");
        result.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_WithCaseInsensitiveHeaders_ParsesCorrectly()
    {
        // Arrange
        var raw = @"## message
This works too.

## patch
```json
{
  ""ops"": [],
  ""meta"": {
    ""agent"": ""test"",
    ""round"": 1,
    ""reason"": ""test""
  }
}
```";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.Message.Should().Contain("This works too");
        result.Patch.Should().NotBeNull();
    }

    [Fact]
    public void Parse_WithExtraWhitespace_TrimsCorrectly()
    {
        // Arrange
        var raw = @"##   MESSAGE   

   This message has extra whitespace.   

##   PATCH   
```json
{
  ""ops"": [],
  ""meta"": {
    ""agent"": ""test"",
    ""round"": 1,
    ""reason"": ""test""
  }
}
```";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.Message.Should().Be("This message has extra whitespace.");
        result.Patch.Should().NotBeNull();
    }

    [Fact]
    public void Parse_WithPatchOnly_ReturnsEmptyMessage()
    {
        // Arrange
        var raw = @"## PATCH
```json
{
  ""ops"": [],
  ""meta"": {
    ""agent"": ""test"",
    ""round"": 1,
    ""reason"": ""test""
  }
}
```";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.Message.Should().BeEmpty();
        result.Patch.Should().NotBeNull();
    }

    [Fact]
    public void Parse_EstimatesTokensCorrectly()
    {
        // Arrange
        var raw = "This is a test message with ten words in total.";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert - ~10 words * 1.3 = ~13 tokens
        result.TokensUsed.Should().BeGreaterThan(0);
        result.TokensUsed.Should().BeInRange(10, 15);
    }

    [Fact]
    public void Parse_WithEmptyString_ReturnsEmptyResponse()
    {
        // Arrange
        var raw = "";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.AgentId.Should().Be("agent");
        result.Message.Should().BeEmpty();
        result.Patch.Should().BeNull();
        result.TokensUsed.Should().Be(0);
    }

    [Fact]
    public void Parse_WithMultilineMessage_PreservesNewlines()
    {
        // Arrange
        var raw = @"## MESSAGE
Line one
Line two
Line three

## PATCH
```json
{
  ""ops"": [],
  ""meta"": {
    ""agent"": ""test"",
    ""round"": 1,
    ""reason"": ""test""
  }
}
```";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.Message.Should().Contain("Line one");
        result.Message.Should().Contain("Line two");
        result.Message.Should().Contain("Line three");
    }

    [Fact]
    public void Parse_WithComplexPatch_DeserializesCorrectly()
    {
        // Arrange
        var raw = @"## MESSAGE
Complex analysis complete.

## PATCH
```json
{
  ""ops"": [
    {
      ""op"": ""add"",
      ""path"": ""/claims/-"",
      ""value"": {
        ""id"": ""claim-1"",
        ""text"": ""Test claim"",
        ""confidence"": 0.8
      }
    }
  ],
  ""meta"": {
    ""agent"": ""gpt-agent"",
    ""round"": 5,
    ""reason"": ""adding validated claim""
  }
}
```";

        // Act
        var result = _parser.Parse(raw, "gpt-agent");

        // Assert
        result.Patch.Should().NotBeNull();
        result.Patch!.Ops.Should().HaveCount(1);
        result.Patch.Ops[0].Op.Should().Be(Agon.Domain.TruthMap.PatchOp.Add);
        result.Patch.Ops[0].Path.Should().Be("/claims/-");
        result.Patch.Meta.Agent.Should().Be("gpt-agent");
        result.Patch.Meta.Round.Should().Be(5);
    }

    [Fact]
    public void Parse_WithJsonOutsideCodeBlock_ReturnsNullPatch()
    {
        // Arrange - JSON not in code block
        var raw = @"## MESSAGE
Message.

## PATCH
{""ops"": [], ""meta"": {""agent"": ""test"", ""round"": 1, ""reason"": ""test""}}";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.Message.Should().Contain("Message");
        result.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_WithNonJsonCodeBlock_ReturnsNullPatch()
    {
        // Arrange
        var raw = @"## MESSAGE
Message.

## PATCH
```python
print('This is not JSON')
```";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.Message.Should().Contain("Message");
        result.Patch.Should().BeNull();
    }

    [Fact]
    public void Parse_PreservesRawOutput()
    {
        // Arrange
        var raw = "Original raw output with special characters: @#$%";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.RawOutput.Should().Be(raw);
    }

    [Fact]
    public void Parse_WithDifferentAgentIds_AssignsCorrectly()
    {
        // Arrange
        var raw = "Test message";

        // Act
        var result1 = _parser.Parse(raw, "gpt-agent");
        var result2 = _parser.Parse(raw, "claude-agent");
        var result3 = _parser.Parse(raw, "gemini-agent");

        // Assert
        result1.AgentId.Should().Be("gpt-agent");
        result2.AgentId.Should().Be("claude-agent");
        result3.AgentId.Should().Be("gemini-agent");
    }

    [Fact]
    public void Parse_TimedOutIsAlwaysFalse()
    {
        // Arrange - Parser never sets TimedOut to true (that's done by the orchestrator)
        var raw = "Test message";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public void Parse_WithVeryLongMessage_HandlesCorrectly()
    {
        // Arrange
        var longMessage = string.Join(" ", Enumerable.Repeat("word", 1000));
        var raw = $@"## MESSAGE
{longMessage}";

        // Act
        var result = _parser.Parse(raw, "agent");

        // Assert
        result.Message.Should().HaveLength(longMessage.Length);
        result.TokensUsed.Should().BeGreaterThan(1000); // ~1300 tokens
    }
}
