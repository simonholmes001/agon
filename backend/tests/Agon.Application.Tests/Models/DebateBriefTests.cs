using Agon.Application.Models;
using FluentAssertions;
using Xunit;

namespace Agon.Application.Tests.Models;

/// <summary>
/// Tests for DebateBrief - output of Socratic Clarifier that seeds the initial Truth Map
/// Coverage Target: 22.2% → 80%
/// </summary>
public sealed class DebateBriefTests
{
    #region Constructor and Property Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldSetAllProperties()
    {
        // Arrange
        var coreIdea = "Build a task management SaaS for remote teams";
        var constraints = new BriefConstraints(
            Budget: "$50k seed",
            Timeline: "3 months MVP",
            TechStack: new[] { "Next.js", "PostgreSQL", "Vercel" },
            NonNegotiables: new[] { "GDPR compliance", "SSO support" }
        );
        var successMetrics = new[] { "100 paying users", "4.5/5 NPS", "<2s page load" };
        var primaryPersona = "Engineering manager at 50-200 person remote-first startups";
        var openQuestions = new[] { "What about mobile?", "Self-hosted option?" };

        // Act
        var brief = new DebateBrief(
            coreIdea,
            constraints,
            successMetrics,
            primaryPersona,
            openQuestions
        );

        // Assert
        brief.CoreIdea.Should().Be(coreIdea);
        brief.Constraints.Should().Be(constraints);
        brief.SuccessMetrics.Should().BeEquivalentTo(successMetrics);
        brief.PrimaryPersona.Should().Be(primaryPersona);
        brief.OpenQuestions.Should().BeEquivalentTo(openQuestions);
    }

    [Fact]
    public void Constructor_WithEmptyCollections_ShouldAllowIt()
    {
        // Arrange
        var coreIdea = "Simple idea";
        var constraints = new BriefConstraints("Any", "Any", Array.Empty<string>(), Array.Empty<string>());

        // Act
        var brief = new DebateBrief(
            coreIdea,
            constraints,
            Array.Empty<string>(),
            "Some persona",
            Array.Empty<string>()
        );

        // Assert
        brief.SuccessMetrics.Should().BeEmpty();
        brief.OpenQuestions.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithNullCoreIdea_ShouldAllowButFailIsComplete()
    {
        // Arrange & Act
        var brief = new DebateBrief(
            null!,
            new BriefConstraints("Any", "Any", Array.Empty<string>(), Array.Empty<string>()),
            new[] { "metric1" },
            "persona",
            Array.Empty<string>()
        );

        // Assert
        brief.CoreIdea.Should().BeNull();
        brief.IsComplete().Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithWhitespaceCoreIdea_ShouldAllowButFailIsComplete()
    {
        // Arrange & Act
        var brief = new DebateBrief(
            "   ",
            new BriefConstraints("Any", "Any", Array.Empty<string>(), Array.Empty<string>()),
            new[] { "metric1" },
            "persona",
            Array.Empty<string>()
        );

        // Assert
        brief.CoreIdea.Should().Be("   ");
        brief.IsComplete().Should().BeFalse();
    }

    #endregion

    #region IsComplete Tests

    [Fact]
    public void IsComplete_WithAllRequiredFields_ShouldReturnTrue()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "Valid idea",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "At least one metric" },
            PrimaryPersona: "Valid persona",
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeTrue();
    }

    [Fact]
    public void IsComplete_WithEmptyCoreIdea_ShouldReturnFalse()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "metric" },
            PrimaryPersona: "persona",
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithNullCoreIdea_ShouldReturnFalse()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: null!,
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "metric" },
            PrimaryPersona: "persona",
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithWhitespaceCoreIdea_ShouldReturnFalse()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "   \t\n",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "metric" },
            PrimaryPersona: "persona",
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithEmptySuccessMetrics_ShouldReturnFalse()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "Valid idea",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: Array.Empty<string>(),
            PrimaryPersona: "persona",
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithEmptyPrimaryPersona_ShouldReturnFalse()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "Valid idea",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "metric" },
            PrimaryPersona: "",
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithNullPrimaryPersona_ShouldReturnFalse()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "Valid idea",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "metric" },
            PrimaryPersona: null!,
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithWhitespacePrimaryPersona_ShouldReturnFalse()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "Valid idea",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "metric" },
            PrimaryPersona: "  \n\t  ",
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeFalse();
    }

    [Fact]
    public void IsComplete_WithMultipleSuccessMetrics_ShouldReturnTrue()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "Valid idea",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "metric1", "metric2", "metric3" },
            PrimaryPersona: "persona",
            OpenQuestions: Array.Empty<string>()
        );

        // Act & Assert
        brief.IsComplete().Should().BeTrue();
    }

    [Fact]
    public void IsComplete_WithOpenQuestions_ShouldStillReturnTrue()
    {
        // OpenQuestions are not required for completeness
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "Valid idea",
            Constraints: new BriefConstraints("Budget", "Timeline", Array.Empty<string>(), Array.Empty<string>()),
            SuccessMetrics: new[] { "metric" },
            PrimaryPersona: "persona",
            OpenQuestions: new[] { "question1", "question2" }
        );

        // Act & Assert
        brief.IsComplete().Should().BeTrue();
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void Equality_WithIdenticalValues_ShouldBeEqual()
    {
        // Note: Record equality with IReadOnlyList compares by reference, not value
        // Arrange
        var constraints = new BriefConstraints("$50k", "3 months", new[] { "Next.js" }, new[] { "GDPR" });
        var metrics = new[] { "metric" };
        var questions = new[] { "question" };
        var brief1 = new DebateBrief("Same idea", constraints, metrics, "persona", questions);
        var brief2 = new DebateBrief("Same idea", constraints, metrics, "persona", questions); // Same references

        // Act & Assert
        brief1.Should().Be(brief2);
        (brief1 == brief2).Should().BeTrue();
    }

    [Fact]
    public void Equality_WithDifferentCoreIdea_ShouldNotBeEqual()
    {
        // Arrange
        var constraints = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>());
        var brief1 = new DebateBrief("Idea A", constraints, new[] { "metric" }, "persona", Array.Empty<string>());
        var brief2 = new DebateBrief("Idea B", constraints, new[] { "metric" }, "persona", Array.Empty<string>());

        // Act & Assert
        brief1.Should().NotBe(brief2);
    }

    [Fact]
    public void Equality_WithDifferentConstraints_ShouldNotBeEqual()
    {
        // Arrange
        var constraints1 = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>());
        var constraints2 = new BriefConstraints("$100k", "6 months", Array.Empty<string>(), Array.Empty<string>());
        var brief1 = new DebateBrief("Same idea", constraints1, new[] { "metric" }, "persona", Array.Empty<string>());
        var brief2 = new DebateBrief("Same idea", constraints2, new[] { "metric" }, "persona", Array.Empty<string>());

        // Act & Assert
        brief1.Should().NotBe(brief2);
    }

    [Fact]
    public void Equality_WithDifferentSuccessMetrics_ShouldNotBeEqual()
    {
        // Arrange
        var constraints = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>());
        var brief1 = new DebateBrief("Same idea", constraints, new[] { "metric1" }, "persona", Array.Empty<string>());
        var brief2 = new DebateBrief("Same idea", constraints, new[] { "metric2" }, "persona", Array.Empty<string>());

        // Act & Assert
        brief1.Should().NotBe(brief2);
    }

    #endregion

    #region Immutability Tests

    [Fact]
    public void WithExpression_ShouldCreateNewInstance()
    {
        // Arrange
        var original = new DebateBrief(
            "Original idea",
            new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>()),
            new[] { "metric" },
            "persona",
            Array.Empty<string>()
        );

        // Act
        var modified = original with { CoreIdea = "Modified idea" };

        // Assert
        original.CoreIdea.Should().Be("Original idea");
        modified.CoreIdea.Should().Be("Modified idea");
        ReferenceEquals(original, modified).Should().BeFalse();
    }

    [Fact]
    public void ReadOnlyCollections_CannotBeModified()
    {
        // Arrange
        var successMetrics = new[] { "metric1", "metric2" };
        var openQuestions = new[] { "question1" };
        var brief = new DebateBrief(
            "idea",
            new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>()),
            successMetrics,
            "persona",
            openQuestions
        );

        // Assert - SuccessMetrics is IReadOnlyList, not modifiable
        brief.SuccessMetrics.Should().BeAssignableTo<IReadOnlyList<string>>();
        brief.OpenQuestions.Should().BeAssignableTo<IReadOnlyList<string>>();
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Constructor_WithVeryLongCoreIdea_ShouldHandleIt()
    {
        // Arrange
        var longIdea = new string('A', 10_000);
        var brief = new DebateBrief(
            longIdea,
            new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>()),
            new[] { "metric" },
            "persona",
            Array.Empty<string>()
        );

        // Act & Assert
        brief.CoreIdea.Should().HaveLength(10_000);
        brief.IsComplete().Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithManySuccessMetrics_ShouldHandleIt()
    {
        // Arrange
        var metrics = Enumerable.Range(1, 100).Select(i => $"Metric {i}").ToArray();
        var brief = new DebateBrief(
            "idea",
            new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>()),
            metrics,
            "persona",
            Array.Empty<string>()
        );

        // Act & Assert
        brief.SuccessMetrics.Should().HaveCount(100);
        brief.IsComplete().Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithUnicodeCharacters_ShouldHandleIt()
    {
        // Arrange
        var brief = new DebateBrief(
            CoreIdea: "Build 一个 task manager for 🚀 teams",
            Constraints: new BriefConstraints("¥500万", "3ヶ月", new[] { "Next.js" }, Array.Empty<string>()),
            SuccessMetrics: new[] { "100 👥 users" },
            PrimaryPersona: "Engineering manager 🧑‍💼",
            OpenQuestions: new[] { "What about モバイル?" }
        );

        // Act & Assert
        brief.CoreIdea.Should().Contain("一个");
        brief.CoreIdea.Should().Contain("🚀");
        brief.IsComplete().Should().BeTrue();
    }

    #endregion
}
