namespace Agon.Application.Services;

/// <summary>
/// Configuration options for generating GitHub Copilot instruction files.
/// </summary>
public class CopilotInstructionOptions
{
    /// <summary>
    /// The glob pattern for the applyTo directive in the YAML frontmatter.
    /// Default is '**' which applies to all files.
    /// </summary>
    public string ApplyTo { get; init; } = "**";
}
