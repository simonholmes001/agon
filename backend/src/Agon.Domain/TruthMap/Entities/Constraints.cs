namespace Agon.Domain.TruthMap.Entities;

public class Constraints
{
    public string? Budget { get; set; }
    public string? Timeline { get; set; }
    public List<string> TechStack { get; set; } = new();
    public List<string> NonNegotiables { get; set; } = new();
}
