using Agon.Application.Models;

namespace Agon.Application.Interfaces;

/// <summary>
/// Canonical document parsing contract for attachment/document workflows.
/// </summary>
public interface IDocumentParser
{
    Task<DocumentParseResult> ParseAsync(
        DocumentParseRequest request,
        CancellationToken cancellationToken = default);
}
