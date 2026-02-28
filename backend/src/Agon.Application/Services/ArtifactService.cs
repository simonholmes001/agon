using Agon.Application.Interfaces;
using Agon.Domain.TruthMap;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text;

namespace Agon.Application.Services;

/// <summary>
/// Service for generating and exporting artifacts from session Truth Maps.
/// </summary>
public class ArtifactService(
    ITruthMapRepository truthMapRepository,
    IEnumerable<IArtifactGenerator> generators,
    ILogger<ArtifactService> logger)
{
    private static readonly Dictionary<ArtifactType, string> ArtifactFileNames = new()
    {
        [ArtifactType.Copilot] = "copilot-instructions.md",
        [ArtifactType.Architecture] = "architecture.instructions.md",
        [ArtifactType.Prd] = "prd.instructions.md",
        [ArtifactType.Risks] = "risk-registry.md",
        [ArtifactType.Assumptions] = "assumption-validation.md",
        [ArtifactType.Verdict] = "verdict.md",
        [ArtifactType.Plan] = "plan.md",
        [ArtifactType.ScenarioDiff] = "scenario-diff.md"
    };

    /// <summary>
    /// Gets the list of artifact types available for generation.
    /// </summary>
    /// <returns>List of available artifact type names.</returns>
    public IReadOnlyList<string> GetAvailableTypes()
    {
        return generators
            .Select(g => g.Type.ToString().ToLowerInvariant())
            .ToList();
    }

    /// <summary>
    /// Generates a specific artifact for a session.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="artifactType">The artifact type to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated artifact content, or null if session not found.</returns>
    public async Task<ArtifactResult?> GenerateArtifactAsync(
        Guid sessionId,
        ArtifactType artifactType,
        CancellationToken cancellationToken)
    {
        var truthMap = await truthMapRepository.GetAsync(sessionId, cancellationToken);
        if (truthMap is null)
        {
            logger.LogWarning(
                "Cannot generate artifact because truth map was not found. SessionId={SessionId} Type={Type}",
                sessionId,
                artifactType);
            return null;
        }

        var generator = generators.FirstOrDefault(g => g.Type == artifactType);
        if (generator is null)
        {
            logger.LogWarning(
                "No generator found for artifact type. SessionId={SessionId} Type={Type}",
                sessionId,
                artifactType);
            return null;
        }

        var content = generator.Generate(truthMap);
        logger.LogInformation(
            "Generated artifact. SessionId={SessionId} Type={Type} ContentLength={ContentLength}",
            sessionId,
            artifactType,
            content.Length);

        return new ArtifactResult(
            sessionId,
            artifactType.ToString().ToLowerInvariant(),
            content,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Exports multiple artifacts as a ZIP archive.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="types">Optional list of types to include. If null or empty, includes all available types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A byte array containing the ZIP archive, or null if session not found.</returns>
    public async Task<byte[]?> ExportArtifactsAsync(
        Guid sessionId,
        IReadOnlyList<ArtifactType>? types,
        CancellationToken cancellationToken)
    {
        var truthMap = await truthMapRepository.GetAsync(sessionId, cancellationToken);
        if (truthMap is null)
        {
            logger.LogWarning(
                "Cannot export artifacts because truth map was not found. SessionId={SessionId}",
                sessionId);
            return null;
        }

        var typesToExport = types?.Count > 0
            ? types
            : generators.Select(g => g.Type).ToList();

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var type in typesToExport)
            {
                var generator = generators.FirstOrDefault(g => g.Type == type);
                if (generator is null)
                {
                    continue;
                }

                var content = generator.Generate(truthMap);
                var fileName = ArtifactFileNames.GetValueOrDefault(type, $"{type.ToString().ToLowerInvariant()}.md");

                var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
            }
        }

        logger.LogInformation(
            "Exported artifacts as ZIP. SessionId={SessionId} TypeCount={TypeCount} ZipSize={ZipSize}",
            sessionId,
            typesToExport.Count,
            memoryStream.Length);

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Tries to parse an artifact type from a string.
    /// </summary>
    /// <param name="typeName">The type name to parse.</param>
    /// <param name="artifactType">The parsed artifact type.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParseArtifactType(string typeName, out ArtifactType artifactType)
    {
        return Enum.TryParse(typeName, ignoreCase: true, out artifactType);
    }
}

/// <summary>
/// Result of generating an artifact.
/// </summary>
/// <param name="SessionId">The session ID.</param>
/// <param name="Type">The artifact type.</param>
/// <param name="Content">The generated content.</param>
/// <param name="GeneratedAtUtc">When the artifact was generated.</param>
public record ArtifactResult(
    Guid SessionId,
    string Type,
    string Content,
    DateTimeOffset GeneratedAtUtc);
