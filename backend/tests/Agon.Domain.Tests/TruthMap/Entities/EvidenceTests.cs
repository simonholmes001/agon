using Agon.Domain.TruthMap.Entities;
using FluentAssertions;

namespace Agon.Domain.Tests.TruthMap.Entities;

public class EvidenceTests
{
    [Fact]
    public void Evidence_CanBeCreatedWithAllProperties()
    {
        // Arrange
        var retrievedAt = new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero);
        
        // Act
        var evidence = new Evidence(
            Id: "evidence-1",
            Title: "Market research study on SaaS pricing",
            Source: "https://example.com/research/saas-pricing-2026",
            RetrievedAt: retrievedAt,
            Summary: "Study shows that 70% of successful SaaS companies use tiered pricing",
            Supports: new[] { "claim-1", "assumption-2" },
            Contradicts: new[] { "claim-5" }
        );

        // Assert
        evidence.Id.Should().Be("evidence-1");
        evidence.Title.Should().Be("Market research study on SaaS pricing");
        evidence.Source.Should().Be("https://example.com/research/saas-pricing-2026");
        evidence.RetrievedAt.Should().Be(retrievedAt);
        evidence.Summary.Should().Be("Study shows that 70% of successful SaaS companies use tiered pricing");
        evidence.Supports.Should().Equal("claim-1", "assumption-2");
        evidence.Contradicts.Should().Equal("claim-5");
    }

    [Fact]
    public void Evidence_WithEmptySupports_IsValid()
    {
        // Arrange & Act
        var evidence = new Evidence(
            "evidence-2",
            "Neutral data point",
            "https://example.com/data",
            DateTimeOffset.UtcNow,
            "This data point doesn't support or contradict any existing claims",
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Assert
        evidence.Supports.Should().BeEmpty();
        evidence.Contradicts.Should().BeEmpty();
    }

    [Fact]
    public void Evidence_WithMultipleSupports_PreservesOrder()
    {
        // Arrange
        var supports = new[] { "claim-1", "claim-2", "assumption-3", "decision-4" };

        // Act
        var evidence = new Evidence(
            "evidence-3",
            "Comprehensive study",
            "https://example.com/study",
            DateTimeOffset.UtcNow,
            "This study validates multiple existing claims",
            supports,
            Array.Empty<string>()
        );

        // Assert
        evidence.Supports.Should().Equal(supports);
        evidence.Supports.Should().HaveCount(4);
    }

    [Fact]
    public void Evidence_WithMultipleContradicts_PreservesOrder()
    {
        // Arrange
        var contradicts = new[] { "claim-10", "assumption-5" };

        // Act
        var evidence = new Evidence(
            "evidence-4",
            "Counter-study",
            "https://example.com/counter-study",
            DateTimeOffset.UtcNow,
            "This study contradicts previous assumptions",
            Array.Empty<string>(),
            contradicts
        );

        // Assert
        evidence.Contradicts.Should().Equal(contradicts);
        evidence.Contradicts.Should().HaveCount(2);
    }

    [Fact]
    public void Evidence_RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var retrievedAt = DateTimeOffset.UtcNow;
        var supports = new[] { "claim-1" };
        var contradicts = new[] { "claim-2" };

        var evidence1 = new Evidence(
            "evidence-1",
            "Study title",
            "https://example.com",
            retrievedAt,
            "Summary text",
            supports,
            contradicts
        );

        var evidence2 = new Evidence(
            "evidence-1",
            "Study title",
            "https://example.com",
            retrievedAt,
            "Summary text",
            supports, // Same array reference
            contradicts // Same array reference
        );

        // Act & Assert
        evidence1.Should().Be(evidence2);
        (evidence1 == evidence2).Should().BeTrue();
    }

    [Fact]
    public void Evidence_RecordEquality_DifferentIds_AreNotEqual()
    {
        // Arrange
        var evidence1 = new Evidence("evidence-1", "title", "source", DateTimeOffset.UtcNow, "summary", Array.Empty<string>(), Array.Empty<string>());
        var evidence2 = new Evidence("evidence-2", "title", "source", DateTimeOffset.UtcNow, "summary", Array.Empty<string>(), Array.Empty<string>());

        // Act & Assert
        evidence1.Should().NotBe(evidence2);
        (evidence1 == evidence2).Should().BeFalse();
    }

    [Fact]
    public void Evidence_WithModification_CreatesNewInstance()
    {
        // Arrange
        var original = new Evidence(
            "evidence-1",
            "Original title",
            "https://original.com",
            DateTimeOffset.UtcNow,
            "Original summary",
            new[] { "claim-1" },
            Array.Empty<string>()
        );

        // Act
        var modified = original with 
        { 
            Title = "Modified title", 
            Summary = "Modified summary" 
        };

        // Assert
        modified.Should().NotBe(original);
        modified.Title.Should().Be("Modified title");
        modified.Summary.Should().Be("Modified summary");
        modified.Id.Should().Be(original.Id); // Other properties preserved
        modified.Source.Should().Be(original.Source);
    }

    [Fact]
    public void Evidence_RetrievedAt_CanBePastDate()
    {
        // Arrange
        var pastDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        var evidence = new Evidence(
            "evidence-historical",
            "Historical study",
            "https://example.com/old-study",
            pastDate,
            "Study from 2020",
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Assert
        evidence.RetrievedAt.Should().Be(pastDate);
        evidence.RetrievedAt.Year.Should().Be(2020);
    }

    [Fact]
    public void Evidence_RetrievedAt_CanBeFutureDate()
    {
        // Arrange
        var futureDate = new DateTimeOffset(2030, 12, 31, 23, 59, 59, TimeSpan.Zero);

        // Act
        var evidence = new Evidence(
            "evidence-future",
            "Scheduled publication",
            "https://example.com/future-study",
            futureDate,
            "Study scheduled for 2030",
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Assert
        evidence.RetrievedAt.Should().Be(futureDate);
        evidence.RetrievedAt.Year.Should().Be(2030);
    }

    [Fact]
    public void Evidence_Source_CanBeAnyString()
    {
        // Arrange & Act
        var webEvidence = new Evidence("e1", "Web", "https://example.com", DateTimeOffset.UtcNow, "Web source", Array.Empty<string>(), Array.Empty<string>());
        var bookEvidence = new Evidence("e2", "Book", "ISBN: 978-0-123456-78-9", DateTimeOffset.UtcNow, "Book citation", Array.Empty<string>(), Array.Empty<string>());
        var internalEvidence = new Evidence("e3", "Internal", "Internal research doc #42", DateTimeOffset.UtcNow, "Company research", Array.Empty<string>(), Array.Empty<string>());

        // Assert
        webEvidence.Source.Should().StartWith("https://");
        bookEvidence.Source.Should().StartWith("ISBN:");
        internalEvidence.Source.Should().Contain("Internal research");
    }

    [Fact]
    public void Evidence_SupportsAndContradicts_CanBothHaveValues()
    {
        // Arrange & Act
        var evidence = new Evidence(
            "evidence-mixed",
            "Nuanced study",
            "https://example.com/nuanced",
            DateTimeOffset.UtcNow,
            "This study supports some claims while contradicting others",
            new[] { "claim-1", "claim-2" },
            new[] { "claim-3", "assumption-4" }
        );

        // Assert
        evidence.Supports.Should().HaveCount(2);
        evidence.Contradicts.Should().HaveCount(2);
        evidence.Supports.Should().NotIntersectWith(evidence.Contradicts);
    }

    [Fact]
    public void Evidence_Deconstruction_WorksCorrectly()
    {
        // Arrange
        var retrievedAt = DateTimeOffset.UtcNow;
        var evidence = new Evidence(
            "evidence-1",
            "Test title",
            "https://test.com",
            retrievedAt,
            "Test summary",
            new[] { "claim-1" },
            new[] { "claim-2" }
        );

        // Act
        var (id, title, source, retrieved, summary, supports, contradicts) = evidence;

        // Assert
        id.Should().Be("evidence-1");
        title.Should().Be("Test title");
        source.Should().Be("https://test.com");
        retrieved.Should().Be(retrievedAt);
        summary.Should().Be("Test summary");
        supports.Should().Equal("claim-1");
        contradicts.Should().Equal("claim-2");
    }

    [Fact]
    public void Evidence_WithLongSummary_IsValid()
    {
        // Arrange
        var longSummary = string.Join(" ", Enumerable.Repeat("This is a very detailed summary with many findings and insights.", 20));

        // Act
        var evidence = new Evidence(
            "evidence-detailed",
            "Comprehensive research paper",
            "https://example.com/detailed",
            DateTimeOffset.UtcNow,
            longSummary,
            new[] { "claim-1" },
            Array.Empty<string>()
        );

        // Assert
        evidence.Summary.Should().HaveLength(longSummary.Length);
        evidence.Summary.Should().Contain("detailed summary");
    }

    [Fact]
    public void Evidence_WithUtcTime_PreservesUtcOffset()
    {
        // Arrange
        var utcTime = DateTimeOffset.UtcNow;

        // Act
        var evidence = new Evidence(
            "evidence-utc",
            "UTC timestamp test",
            "https://example.com",
            utcTime,
            "Testing UTC preservation",
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Assert
        evidence.RetrievedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Evidence_WithNonUtcTime_PreservesOffset()
    {
        // Arrange
        var pstOffset = TimeSpan.FromHours(-8);
        var pstTime = new DateTimeOffset(2026, 3, 7, 12, 0, 0, pstOffset);

        // Act
        var evidence = new Evidence(
            "evidence-pst",
            "PST timestamp test",
            "https://example.com",
            pstTime,
            "Testing PST preservation",
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Assert
        evidence.RetrievedAt.Offset.Should().Be(pstOffset);
    }

    [Fact]
    public void Evidence_SupportsAndContradicts_AreMutuallyExclusive()
    {
        // Arrange
        var supports = new[] { "claim-1", "claim-2", "claim-3" };
        var contradicts = new[] { "claim-4", "claim-5" };

        // Act
        var evidence = new Evidence(
            "evidence-exclusive",
            "Mutually exclusive test",
            "https://example.com",
            DateTimeOffset.UtcNow,
            "Testing supports and contradicts are different",
            supports,
            contradicts
        );

        // Assert
        evidence.Supports.Should().NotContain(evidence.Contradicts);
        evidence.Contradicts.Should().NotContain(evidence.Supports);
    }
}
