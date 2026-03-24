namespace Agon.Api.Auth;

public sealed record AuthStatusResponse(
    bool Required,
    string Scheme,
    string? Authority,
    string? Audience,
    string? TenantId,
    string? Scope);

public static class AuthStatusResponseFactory
{
    private static readonly HashSet<string> TenantAliases =
    [
        "common",
        "organizations",
        "consumers"
    ];

    public static AuthStatusResponse Create(
        bool authEnabled,
        string authority,
        string audience,
        string tenantIdHint)
    {
        if (!authEnabled)
        {
            return new AuthStatusResponse(
                Required: false,
                Scheme: "none",
                Authority: null,
                Audience: null,
                TenantId: null,
                Scope: null);
        }

        var normalizedAuthority = Normalize(authority);
        var normalizedAudience = Normalize(audience);
        var tenantId = ResolveTenantId(normalizedAuthority, Normalize(tenantIdHint));
        var scope = BuildDefaultScope(normalizedAudience);

        return new AuthStatusResponse(
            Required: true,
            Scheme: "bearer",
            Authority: normalizedAuthority,
            Audience: normalizedAudience,
            TenantId: tenantId,
            Scope: scope);
    }

    private static string? ResolveTenantId(string? authority, string? tenantIdHint)
    {
        if (!string.IsNullOrWhiteSpace(tenantIdHint))
        {
            return tenantIdHint;
        }

        if (string.IsNullOrWhiteSpace(authority))
        {
            return null;
        }

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri))
        {
            return null;
        }

        var firstSegment = authorityUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstSegment))
        {
            return null;
        }

        if (TenantAliases.Contains(firstSegment))
        {
            return null;
        }

        return firstSegment;
    }

    private static string? BuildDefaultScope(string? audience)
    {
        if (string.IsNullOrWhiteSpace(audience))
        {
            return null;
        }

        if (audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
        {
            return audience;
        }

        if (Guid.TryParse(audience, out _))
        {
            return FormattableString.Invariant($"api://{audience}/.default");
        }

        if (audience.Contains("://", StringComparison.Ordinal))
        {
            var trimmed = audience.TrimEnd('/');
            return FormattableString.Invariant($"{trimmed}/.default");
        }

        return FormattableString.Invariant($"api://{audience}/.default");
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
