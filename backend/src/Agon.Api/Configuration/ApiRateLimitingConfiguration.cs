namespace Agon.Api.Configuration;

/// <summary>
/// Runtime configuration for API rate limiting guardrails.
/// </summary>
public sealed class ApiRateLimitingConfiguration
{
    public const string SectionName = "ApiRateLimiting";

    public bool Enabled { get; set; } = true;

    public EndpointRateLimitConfiguration SessionCreate { get; set; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 60,
        QueueLimit = 0
    };

    public EndpointRateLimitConfiguration SessionMessage { get; set; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 60,
        QueueLimit = 0
    };

    public EndpointRateLimitConfiguration AttachmentUpload { get; set; } = new()
    {
        PermitLimit = 12,
        WindowSeconds = 60,
        QueueLimit = 0
    };
}

public sealed class EndpointRateLimitConfiguration
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
}
