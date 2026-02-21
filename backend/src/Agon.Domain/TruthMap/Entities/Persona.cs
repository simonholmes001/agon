namespace Agon.Domain.TruthMap.Entities;

public class Persona
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
}
