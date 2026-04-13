using Agon.Domain.Sessions;
using Agon.Application.Orchestration;
using Agon.Api.Configuration;
using Agon.Infrastructure.Attachments;
using FluentAssertions;
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

    [Fact]
    public void AttachmentChunkLoopOptions_Should_Be_Registered_As_Singleton()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetService<AttachmentChunkLoopOptions>();

        options.Should().NotBeNull("chunk-loop options must be registered for runtime tuning");
        options.Should().BeOfType<AttachmentChunkLoopOptions>();
    }

    [Fact]
    public void AttachmentChunkLoopOptions_Should_Respect_Configuration_Overrides()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AttachmentProcessing:ChunkLoop:Enabled", "false");
            builder.UseSetting("AttachmentProcessing:ChunkLoop:ActivationThresholdChars", "8000");
            builder.UseSetting("AttachmentProcessing:ChunkLoop:ChunkSizeChars", "6000");
            builder.UseSetting("AttachmentProcessing:ChunkLoop:ChunkOverlapChars", "400");
            builder.UseSetting("AttachmentProcessing:ChunkLoop:MaxChunksPerAttachment", "12");
            builder.UseSetting("AttachmentProcessing:ChunkLoop:MaxChunkNoteChars", "900");
            builder.UseSetting("AttachmentProcessing:ChunkLoop:MaxFinalNotesPerAgent", "6");
        });

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<AttachmentChunkLoopOptions>();

        options.Enabled.Should().BeFalse();
        options.ActivationThresholdChars.Should().Be(8000);
        options.ChunkSizeChars.Should().Be(6000);
        options.ChunkOverlapChars.Should().Be(400);
        options.MaxChunksPerAttachment.Should().Be(12);
        options.MaxChunkNoteChars.Should().Be(900);
        options.MaxFinalNotesPerAgent.Should().Be(6);
    }

    [Fact]
    public void AttachmentUploadValidationOptions_Should_Be_Registered_As_Singleton()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetService<AttachmentUploadValidationOptions>();

        options.Should().NotBeNull("upload validation options must be registered for deterministic contracts");
        options.Should().BeOfType<AttachmentUploadValidationOptions>();
    }

    [Fact]
    public void AttachmentUploadValidationOptions_Should_Respect_Configuration_Overrides()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AttachmentProcessing:Validation:RejectUnsupportedFormats", "true");
            builder.UseSetting("AttachmentProcessing:Validation:MaxUploadBytes", "1024");
            builder.UseSetting("AttachmentProcessing:Validation:MaxTextUploadBytes", "128");
            builder.UseSetting("AttachmentProcessing:Validation:MaxDocumentUploadBytes", "256");
            builder.UseSetting("AttachmentProcessing:Validation:MaxImageUploadBytes", "512");
        });

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<AttachmentUploadValidationOptions>();

        options.RejectUnsupportedFormats.Should().BeTrue();
        options.MaxUploadBytes.Should().Be(1024);
        options.MaxTextUploadBytes.Should().Be(128);
        options.MaxDocumentUploadBytes.Should().Be(256);
        options.MaxImageUploadBytes.Should().Be(512);
    }

    [Fact]
    public void AttachmentExtractionOptions_Should_Respect_TransientRetry_Configuration_Overrides()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AttachmentProcessing:TransientRetry:MaxAttempts", "5");
            builder.UseSetting("AttachmentProcessing:TransientRetry:BaseDelayMs", "75");
            builder.UseSetting("AttachmentProcessing:TransientRetry:MaxDelayMs", "900");
        });

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<AttachmentExtractionOptions>();

        options.TransientRetry.MaxAttempts.Should().Be(5);
        options.TransientRetry.BaseDelayMs.Should().Be(75);
        options.TransientRetry.MaxDelayMs.Should().Be(900);
    }
}
