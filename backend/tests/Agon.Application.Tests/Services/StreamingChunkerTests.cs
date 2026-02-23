using System.Linq;
using Agon.Application.Services;
using FluentAssertions;

namespace Agon.Application.Tests.Services;

public class StreamingChunkerTests
{
    [Fact]
    public void BuildSegments_ForShortMessage_ReturnsSingleSegment()
    {
        var message = "Short response.";

        var segments = StreamingChunker.BuildSegments(message);

        segments.Should().HaveCount(1);
        segments[0].Should().Be(message);
    }

    [Fact]
    public void BuildSegments_ForLongMessage_ReturnsManySegmentsAndFullTail()
    {
        var message = string.Join(" ", Enumerable.Repeat("Extended streaming response", 120));

        var segments = StreamingChunker.BuildSegments(message);

        segments.Should().HaveCountGreaterThan(16);
        segments[^1].Should().Be(message);
        segments.Should().OnlyHaveUniqueItems();
        segments.Should().BeInAscendingOrder(segment => segment.Length);
        var maxDelta = segments
            .Skip(1)
            .Select((segment, index) => segment.Length - segments[index].Length)
            .Max();
        maxDelta.Should().BeLessThanOrEqualTo(240);
    }
}
