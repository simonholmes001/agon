using Agon.Application.Models;
using FluentAssertions;
using Xunit;

namespace Agon.Application.Tests.Models;

/// <summary>
/// Tests for BriefConstraints - constraint values extracted during clarification
/// Coverage Target: 40% → 80%
/// </summary>
public sealed class BriefConstraintsTests
{
    #region Constructor and Property Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldSetAllProperties()
    {
        // Arrange
        var budget = "$50k seed funding";
        var timeline = "3 months to MVP";
        var techStack = new[] { "Next.js", "PostgreSQL", "Vercel", "Redis" };
        var nonNegotiables = new[] { "GDPR compliance", "SSO support", "99.9% uptime" };

        // Act
        var constraints = new BriefConstraints(budget, timeline, techStack, nonNegotiables);

        // Assert
        constraints.Budget.Should().Be(budget);
        constraints.Timeline.Should().Be(timeline);
        constraints.TechStack.Should().BeEquivalentTo(techStack);
        constraints.NonNegotiables.Should().BeEquivalentTo(nonNegotiables);
    }

    [Fact]
    public void Constructor_WithEmptyCollections_ShouldAllowIt()
    {
        // Arrange & Act
        var constraints = new BriefConstraints(
            "Flexible budget",
            "No hard deadline",
            Array.Empty<string>(),
            Array.Empty<string>()
        );

        // Assert
        constraints.TechStack.Should().BeEmpty();
        constraints.NonNegotiables.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithNullBudget_ShouldAllowIt()
    {
        // Arrange & Act
        var constraints = new BriefConstraints(null!, "timeline", Array.Empty<string>(), Array.Empty<string>());

        // Assert
        constraints.Budget.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithNullTimeline_ShouldAllowIt()
    {
        // Arrange & Act
        var constraints = new BriefConstraints("budget", null!, Array.Empty<string>(), Array.Empty<string>());

        // Assert
        constraints.Timeline.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithSingleItemCollections_ShouldWork()
    {
        // Arrange & Act
        var constraints = new BriefConstraints(
            "$100k",
            "6 months",
            new[] { "React" },
            new[] { "Accessibility" }
        );

        // Assert
        constraints.TechStack.Should().ContainSingle().Which.Should().Be("React");
        constraints.NonNegotiables.Should().ContainSingle().Which.Should().Be("Accessibility");
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void Equality_WithIdenticalValues_ShouldBeEqual()
    {
        // Note: Record equality with IReadOnlyList compares by reference, not value
        // Arrange
        var techStack = new[] { "Next.js", "PostgreSQL" };
        var nonNegotiables = new[] { "GDPR" };
        var constraints1 = new BriefConstraints("$50k", "3 months", techStack, nonNegotiables);
        var constraints2 = new BriefConstraints("$50k", "3 months", techStack, nonNegotiables); // Same references

        // Act & Assert
        constraints1.Should().Be(constraints2);
        (constraints1 == constraints2).Should().BeTrue();
    }

    [Fact]
    public void Equality_WithDifferentBudget_ShouldNotBeEqual()
    {
        // Arrange
        var constraints1 = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>());
        var constraints2 = new BriefConstraints("$100k", "3 months", Array.Empty<string>(), Array.Empty<string>());

        // Act & Assert
        constraints1.Should().NotBe(constraints2);
    }

    [Fact]
    public void Equality_WithDifferentTimeline_ShouldNotBeEqual()
    {
        // Arrange
        var constraints1 = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>());
        var constraints2 = new BriefConstraints("$50k", "6 months", Array.Empty<string>(), Array.Empty<string>());

        // Act & Assert
        constraints1.Should().NotBe(constraints2);
    }

    [Fact]
    public void Equality_WithDifferentTechStack_ShouldNotBeEqual()
    {
        // Arrange
        var constraints1 = new BriefConstraints("$50k", "3 months", new[] { "React" }, Array.Empty<string>());
        var constraints2 = new BriefConstraints("$50k", "3 months", new[] { "Vue" }, Array.Empty<string>());

        // Act & Assert
        constraints1.Should().NotBe(constraints2);
    }

    [Fact]
    public void Equality_WithDifferentNonNegotiables_ShouldNotBeEqual()
    {
        // Arrange
        var constraints1 = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), new[] { "GDPR" });
        var constraints2 = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), new[] { "HIPAA" });

        // Act & Assert
        constraints1.Should().NotBe(constraints2);
    }

    [Fact]
    public void Equality_WithDifferentTechStackOrder_ShouldNotBeEqual()
    {
        // Record equality is order-sensitive for collections
        // Arrange
        var constraints1 = new BriefConstraints("$50k", "3 months", new[] { "React", "PostgreSQL" }, Array.Empty<string>());
        var constraints2 = new BriefConstraints("$50k", "3 months", new[] { "PostgreSQL", "React" }, Array.Empty<string>());

        // Act & Assert
        constraints1.Should().NotBe(constraints2);
    }

    #endregion

    #region Immutability Tests

    [Fact]
    public void WithExpression_ShouldCreateNewInstance()
    {
        // Arrange
        var original = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), Array.Empty<string>());

        // Act
        var modified = original with { Budget = "$100k" };

        // Assert
        original.Budget.Should().Be("$50k");
        modified.Budget.Should().Be("$100k");
        ReferenceEquals(original, modified).Should().BeFalse();
    }

    [Fact]
    public void ReadOnlyCollections_CannotBeModified()
    {
        // Arrange
        var techStack = new[] { "Next.js", "PostgreSQL" };
        var nonNegotiables = new[] { "GDPR", "SOC2" };
        var constraints = new BriefConstraints("$50k", "3 months", techStack, nonNegotiables);

        // Assert - Collections are IReadOnlyList, not modifiable
        constraints.TechStack.Should().BeAssignableTo<IReadOnlyList<string>>();
        constraints.NonNegotiables.Should().BeAssignableTo<IReadOnlyList<string>>();
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Constructor_WithVeryLongBudgetString_ShouldHandleIt()
    {
        // Arrange
        var longBudget = new string('$', 1000) + " (with detailed breakdown: " + new string('.', 5000) + ")";
        
        // Act
        var constraints = new BriefConstraints(longBudget, "timeline", Array.Empty<string>(), Array.Empty<string>());

        // Assert
        constraints.Budget.Should().HaveLength(longBudget.Length);
    }

    [Fact]
    public void Constructor_WithManyTechStackItems_ShouldHandleIt()
    {
        // Arrange
        var manyTechs = Enumerable.Range(1, 100).Select(i => $"Tech{i}").ToArray();
        
        // Act
        var constraints = new BriefConstraints("$50k", "3 months", manyTechs, Array.Empty<string>());

        // Assert
        constraints.TechStack.Should().HaveCount(100);
    }

    [Fact]
    public void Constructor_WithManyNonNegotiables_ShouldHandleIt()
    {
        // Arrange
        var manyNonNegotiables = Enumerable.Range(1, 50).Select(i => $"Requirement{i}").ToArray();
        
        // Act
        var constraints = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), manyNonNegotiables);

        // Assert
        constraints.NonNegotiables.Should().HaveCount(50);
    }

    [Fact]
    public void Constructor_WithUnicodeCharacters_ShouldHandleIt()
    {
        // Arrange & Act
        var constraints = new BriefConstraints(
            Budget: "¥5,000,000 (五百萬円)",
            Timeline: "3ヶ月 (three months)",
            TechStack: new[] { "Next.js", "PostgreSQL", "云服务器" },
            NonNegotiables: new[] { "GDPR 🇪🇺", "プライバシー保護", "可访问性 ♿" }
        );

        // Assert
        constraints.Budget.Should().Contain("¥");
        constraints.Budget.Should().Contain("五百萬円");
        constraints.Timeline.Should().Contain("ヶ月");
        constraints.TechStack.Should().Contain("云服务器");
        constraints.NonNegotiables.Should().Contain("GDPR 🇪🇺");
    }

    [Fact]
    public void Constructor_WithSpecialCharactersInStrings_ShouldHandleIt()
    {
        // Arrange & Act
        var constraints = new BriefConstraints(
            Budget: "$50k + equity (10-15%)",
            Timeline: "Q1 2026 -> Q2 2026 (hard deadline!)",
            TechStack: new[] { "Node.js@20.x", "PostgreSQL>=15", "Redis~7.2" },
            NonNegotiables: new[] { "GDPR/CCPA", "SSO (SAML/OAuth)", "99.9% SLA" }
        );

        // Assert
        constraints.Budget.Should().Contain("$50k + equity (10-15%)");
        constraints.Timeline.Should().Contain("->");
        constraints.TechStack.Should().Contain("Node.js@20.x");
    }

    [Fact]
    public void Constructor_WithEmptyStrings_ShouldAllowIt()
    {
        // Arrange & Act
        var constraints = new BriefConstraints("", "", Array.Empty<string>(), Array.Empty<string>());

        // Assert
        constraints.Budget.Should().BeEmpty();
        constraints.Timeline.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithWhitespaceStrings_ShouldAllowIt()
    {
        // Arrange & Act
        var constraints = new BriefConstraints("   ", "\t\n", Array.Empty<string>(), Array.Empty<string>());

        // Assert
        constraints.Budget.Should().Be("   ");
        constraints.Timeline.Should().Be("\t\n");
    }

    #endregion

    #region Business Logic Tests

    [Fact]
    public void TechStack_CanContainDuplicates_IfProvided()
    {
        // This tests that the model doesn't enforce uniqueness
        // (that's a concern for the clarifier logic, not the model)
        // Arrange
        var techStack = new[] { "React", "React", "PostgreSQL" };
        
        // Act
        var constraints = new BriefConstraints("$50k", "3 months", techStack, Array.Empty<string>());

        // Assert
        constraints.TechStack.Should().HaveCount(3);
        constraints.TechStack.Where(t => t == "React").Should().HaveCount(2);
    }

    [Fact]
    public void NonNegotiables_CanContainDuplicates_IfProvided()
    {
        // Arrange
        var nonNegotiables = new[] { "GDPR", "GDPR", "SOC2" };
        
        // Act
        var constraints = new BriefConstraints("$50k", "3 months", Array.Empty<string>(), nonNegotiables);

        // Assert
        constraints.NonNegotiables.Should().HaveCount(3);
        constraints.NonNegotiables.Where(n => n == "GDPR").Should().HaveCount(2);
    }

    #endregion
}
