using Agon.Domain.Sessions;
using Agon.Domain.TruthMap;

namespace Agon.Application.Orchestration;

public class AgentContext
{
    public Guid SessionId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public int Round { get; init; }
    public SessionPhase Phase { get; init; }
    public int FrictionLevel { get; init; }
    public required TruthMapState TruthMap { get; init; }
    public IReadOnlyList<string> ContestedClaims { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MicroDirectives { get; init; } = Array.Empty<string>();
}
