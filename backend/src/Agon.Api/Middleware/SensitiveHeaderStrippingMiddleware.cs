namespace Agon.Api.Middleware;

/// <summary>
/// Middleware that strips inbound sensitive provider key headers before they can reach any handler.
///
/// The CLI historically sent per-request provider API keys via <c>X-Agon-Provider-Key-*</c> headers.
/// The backend uses server-managed keys exclusively (server-side BYOK model), so these headers
/// are never consumed. Stripping them at the pipeline entry point ensures:
///   - Key material is never logged by ASP.NET Core's request logging or telemetry.
///   - Key material is never included in exception details or problem responses.
///   - No accidental future consumption from middleware or handlers.
///
/// <see href="https://github.com/simonholmes001/agon/issues/381">Issue #381</see>
/// <see href="https://github.com/simonholmes001/agon/issues/382">Issue #382</see>
/// </summary>
public sealed class SensitiveHeaderStrippingMiddleware
{
    /// <summary>The header prefix used for per-provider API key transport.</summary>
    internal const string ProviderKeyHeaderPrefix = "X-Agon-Provider-Key-";

    private readonly RequestDelegate _next;
    private readonly ILogger<SensitiveHeaderStrippingMiddleware> _logger;

    public SensitiveHeaderStrippingMiddleware(
        RequestDelegate next,
        ILogger<SensitiveHeaderStrippingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stripped = StripSensitiveHeaders(context.Request.Headers);
        if (stripped > 0)
        {
            // Log count only — never log header names that could reveal which provider is in use,
            // and never log header values.
            _logger.LogWarning(
                "Stripped {Count} sensitive provider key header(s) from inbound request. " +
                "Backend uses server-managed keys; client-supplied provider keys are not accepted.",
                stripped);
        }

        await _next(context);
    }

    public static int StripSensitiveHeaders(IHeaderDictionary headers)
    {
        var toRemove = headers.Keys
            .Where(k => k.StartsWith(ProviderKeyHeaderPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in toRemove)
        {
            headers.Remove(key);
        }

        return toRemove.Count;
    }
}
