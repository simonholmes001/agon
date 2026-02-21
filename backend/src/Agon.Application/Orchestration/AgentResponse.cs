using Agon.Domain.TruthMap;

namespace Agon.Application.Orchestration;

public class AgentResponse
{
    public string Message { get; init; } = string.Empty;
    public TruthMapPatch? Patch { get; init; }
    public string? RawOutput { get; init; }
}
