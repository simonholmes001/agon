using Agon.Domain.TruthMap;

namespace Agon.Application.Interfaces;

/// <summary>
/// Interface for generating various artifact formats from Truth Map state.
/// </summary>
public interface IArtifactGenerator
{
    /// <summary>
    /// Gets the artifact type this generator produces.
    /// </summary>
    ArtifactType Type { get; }

    /// <summary>
    /// Generates the artifact content from the Truth Map.
    /// </summary>
    /// <param name="truthMap">The Truth Map state to transform.</param>
    /// <returns>The generated artifact content as a string.</returns>
    string Generate(TruthMapState truthMap);
}

/// <summary>
/// Enumeration of artifact types produced by the system.
/// </summary>
public enum ArtifactType
{
    /// <summary>Go/No-Go verdict with rationale and contested claims.</summary>
    Verdict,
    
    /// <summary>Phased plan (MVP / v1 / v2).</summary>
    Plan,
    
    /// <summary>Product Requirements Document.</summary>
    Prd,
    
    /// <summary>Risk registry with mitigations.</summary>
    Risks,
    
    /// <summary>Assumption validation table.</summary>
    Assumptions,
    
    /// <summary>GitHub Copilot instructions file.</summary>
    Copilot,
    
    /// <summary>Mermaid architecture diagram.</summary>
    Architecture,
    
    /// <summary>Scenario diff for fork comparisons.</summary>
    ScenarioDiff
}
