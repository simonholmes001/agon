using Agon.Domain.Sessions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Agon.Integration.Tests;

/// <summary>
/// Integration tests for Program.cs startup configuration and DI registration.
/// These tests verify that the application starts successfully and all required services are registered.
/// 
/// TDD Approach:
/// 1. RED: Write a failing test that verifies a service is registered
/// 2. GREEN: Add the registration to Program.cs to make the test pass
/// 3. REFACTOR: Clean up if needed
/// </summary>
public class ProgramIntegrationTests : IClassFixture<AgonWebApplicationFactory>
{
    private readonly AgonWebApplicationFactory _factory;

    public ProgramIntegrationTests(AgonWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Application_Should_Start_Successfully()
    {
        // Arrange & Act
        var client = _factory.CreateClient();

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void RoundPolicy_Should_Be_Registered_As_Singleton()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Act
        var roundPolicy = serviceProvider.GetService<RoundPolicy>();

        // Assert
        roundPolicy.Should().NotBeNull("RoundPolicy must be registered in DI container");
        roundPolicy.Should().BeOfType<RoundPolicy>();
    }

    [Fact]
    public void RoundPolicy_Should_Have_Configuration_Values_From_AppSettings()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Act
        var roundPolicy = serviceProvider.GetRequiredService<RoundPolicy>();

        // Assert
        roundPolicy.MaxClarificationRounds.Should().BeGreaterThan(0, "MaxClarificationRounds must be configured");
        roundPolicy.MaxDebateRounds.Should().BeGreaterThan(0, "MaxDebateRounds must be configured");
        roundPolicy.MaxTargetedLoops.Should().BeGreaterThan(0, "MaxTargetedLoops must be configured");
        roundPolicy.MaxSessionBudgetTokens.Should().BeGreaterThan(0, "MaxSessionBudgetTokens must be configured");
        roundPolicy.ConvergenceThresholdStandard.Should().BeInRange(0f, 1f, "ConvergenceThresholdStandard must be a valid percentage");
        roundPolicy.HighFrictionCutoff.Should().BeInRange(0, 100, "HighFrictionCutoff must be a valid friction level");
    }

    [Fact]
    public void RoundPolicy_Should_Return_Same_Instance_For_Multiple_Requests()
    {
        // Arrange
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        // Act
        var policy1 = scope1.ServiceProvider.GetRequiredService<RoundPolicy>();
        var policy2 = scope2.ServiceProvider.GetRequiredService<RoundPolicy>();

        // Assert
        policy1.Should().BeSameAs(policy2, "RoundPolicy is registered as Singleton and should return the same instance");
    }
}
