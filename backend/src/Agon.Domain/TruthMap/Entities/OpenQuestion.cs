namespace Agon.Domain.TruthMap.Entities;

public class OpenQuestion
{
    public required string Id { get; init; }
    public required string Text { get; set; }
    public bool Blocking { get; set; }
    public required string RaisedBy { get; init; }
}
