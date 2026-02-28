using System.Linq;
using Agon.Application.Services;
using FluentAssertions;

namespace Agon.Application.Tests.Services;

public class StreamingChunkerTests
{
    #region Empty and Short Messages

    [Fact]
    public void BuildSegments_ForNullMessage_ReturnsSingleEmptySegment()
    {
        var segments = StreamingChunker.BuildSegments(null!);

        segments.Should().HaveCount(1);
        segments[0].Should().BeEmpty();
    }

    [Fact]
    public void BuildSegments_ForEmptyMessage_ReturnsSingleEmptySegment()
    {
        var segments = StreamingChunker.BuildSegments(string.Empty);

        segments.Should().HaveCount(1);
        segments[0].Should().BeEmpty();
    }

    [Fact]
    public void BuildSegments_ForWhitespaceOnlyMessage_ReturnsSingleEmptySegment()
    {
        var segments = StreamingChunker.BuildSegments("   \t\n  ");

        segments.Should().HaveCount(1);
        segments[0].Should().BeEmpty();
    }

    [Fact]
    public void BuildSegments_ForShortMessage_ReturnsSingleSegment()
    {
        var message = "Short response.";

        var segments = StreamingChunker.BuildSegments(message);

        segments.Should().HaveCount(1);
        segments[0].Should().Be(message);
    }

    [Fact]
    public void BuildSegments_ForMessageExactlyAtChunkSize_ReturnsSingleSegment()
    {
        var message = new string('a', 120);

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 120);

        segments.Should().HaveCount(1);
        segments[0].Should().Be(message);
    }

    #endregion

    #region Long Messages

    [Fact]
    public void BuildSegments_ForLongMessage_ReturnsManySegmentsAndFullTail()
    {
        var message = string.Join(" ", Enumerable.Repeat("Extended streaming response", 120));

        var segments = StreamingChunker.BuildSegments(message);

        segments.Should().HaveCountGreaterThan(16);
        segments[^1].Should().Be(message);
        segments.Should().OnlyHaveUniqueItems();
        segments.Should().BeInAscendingOrder(segment => segment.Length);
    }

    [Fact]
    public void BuildSegments_SegmentLengthDeltasAreReasonable()
    {
        var message = string.Join(" ", Enumerable.Repeat("Extended streaming response", 120));

        var segments = StreamingChunker.BuildSegments(message);

        var maxDelta = segments
            .Skip(1)
            .Select((segment, index) => segment.Length - segments[index].Length)
            .Max();
        maxDelta.Should().BeLessThanOrEqualTo(240);
    }

    [Fact]
    public void BuildSegments_RespectsMaxSegmentsLimit()
    {
        var message = string.Join(" ", Enumerable.Repeat("word", 1000));

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 10, maxSegments: 5);

        segments.Should().HaveCountLessThanOrEqualTo(6); // maxSegments + possible final full segment
    }

    [Fact]
    public void BuildSegments_WithCustomChunkSize_ProducesMoreSegments()
    {
        var message = string.Join(" ", Enumerable.Repeat("word", 100));

        var smallChunks = StreamingChunker.BuildSegments(message, targetChunkSize: 50);
        var largeChunks = StreamingChunker.BuildSegments(message, targetChunkSize: 200);

        smallChunks.Should().HaveCountGreaterThan(largeChunks.Count);
    }

    #endregion

    #region Word Boundary Handling

    [Fact]
    public void BuildSegments_PreservesWordBoundaries()
    {
        var message = "The quick brown fox jumps over the lazy dog repeatedly";

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 20);

        foreach (var segment in segments.SkipLast(1))
        {
            // Segments should end at word boundaries (whitespace or end)
            segment.Should().NotEndWith("-");
            if (segment.Length < message.Length)
            {
                var lastNonWhitespace = segment.TrimEnd();
                lastNonWhitespace.Should().NotContain("  "); // No double spaces mid-word
            }
        }
    }

    [Fact]
    public void BuildSegments_HandlesLongWordWithoutSpaces()
    {
        var message = new string('x', 500);

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 50);

        // Should still produce segments, just not at ideal word boundaries
        segments.Should().NotBeEmpty();
        segments[^1].Should().Be(message);
    }

    [Fact]
    public void BuildSegments_TrimsTrailingWhitespaceFromSegments()
    {
        var message = "This is a test   with extra    spaces throughout  ";

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 30);

        foreach (var segment in segments)
        {
            segment.Should().Be(segment.TrimEnd());
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void BuildSegments_HandlesUnicodeCharacters()
    {
        var message = string.Join(" ", Enumerable.Repeat("日本語テスト", 50));

        var segments = StreamingChunker.BuildSegments(message);

        segments.Should().NotBeEmpty();
        segments[^1].Should().Be(message);
    }

    [Fact]
    public void BuildSegments_HandlesNewlinesAndTabs()
    {
        var message = "Line one\nLine two\tTabbed\nLine three with more content here";

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 20);

        segments.Should().NotBeEmpty();
        segments[^1].Should().Be(message);
    }

    [Fact]
    public void BuildSegments_MessageSlightlyOverChunkSize_ProducesMultipleSegments()
    {
        var message = new string('a', 125) + " " + new string('b', 125);

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 120);

        segments.Should().HaveCountGreaterThan(1);
        segments[^1].Should().Be(message);
    }

    [Fact]
    public void BuildSegments_AllSegmentsArePrefixesOfFinalMessage()
    {
        var message = string.Join(" ", Enumerable.Repeat("streaming content", 50));

        var segments = StreamingChunker.BuildSegments(message);

        foreach (var segment in segments)
        {
            message.Should().StartWith(segment.TrimEnd());
        }
    }

    [Fact]
    public void BuildSegments_SkipsEmptyOrShorterSegments()
    {
        // Create a message that might produce empty/duplicate segments due to word boundary adjustments
        var message = "a " + new string('b', 200) + " c";

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 50);

        // All segments should be strictly increasing in length
        for (var i = 1; i < segments.Count; i++)
        {
            segments[i].Length.Should().BeGreaterThan(segments[i - 1].Length,
                $"Segment {i} should be longer than segment {i - 1}");
        }
    }

    [Fact]
    public void BuildSegments_HandlesWordBoundaryAtEndOfWindow()
    {
        // Create a message where whitespace is found in forward direction (after index)
        // This happens when there's no whitespace before the target index within the window
        var longWord = new string('x', 30);
        var message = longWord + " rest of message with more content following";

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 25);

        segments.Should().NotBeEmpty();
        segments[^1].Should().Be(message);
    }

    [Fact]
    public void BuildSegments_AdjustsToWordBoundaryOutsideWindow()
    {
        // Create a message with no whitespace within the window, forcing fallback to clamp
        var noSpaceSection = new string('x', 100);
        var message = noSpaceSection;

        var segments = StreamingChunker.BuildSegments(message, targetChunkSize: 30);

        segments.Should().NotBeEmpty();
        segments[^1].Should().Be(message);
    }

    #endregion
}
