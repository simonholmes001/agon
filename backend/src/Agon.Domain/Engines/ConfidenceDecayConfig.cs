namespace Agon.Domain.Engines;

/// <summary>
/// Configuration for confidence decay behaviour.
/// Defaults from the schemas specification.
/// </summary>
public class ConfidenceDecayConfig
{
    public float DecayStep { get; init; } = 0.15f;
    public float BoostStep { get; init; } = 0.10f;
    public float ContestedThreshold { get; init; } = 0.30f;
}
