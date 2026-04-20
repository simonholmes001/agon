using System.Text.RegularExpressions;
using Agon.Api.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace Agon.Api.Middleware;

/// <summary>
/// Enforces a minimum supported Agon CLI version for API requests.
/// </summary>
public sealed class MinimumCliVersionMiddleware
{
    private const string CliVersionHeader = "X-Agon-CLI-Version";
    private static readonly Regex SemverPattern =
        new(@"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
            RegexOptions.Compiled);

    private readonly RequestDelegate _next;
    private readonly ILogger<MinimumCliVersionMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private readonly AgonConfiguration _agonConfig;

    public MinimumCliVersionMiddleware(
        RequestDelegate next,
        ILogger<MinimumCliVersionMiddleware> logger,
        IHostEnvironment environment,
        AgonConfiguration agonConfig)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _agonConfig = agonConfig;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldBypass(context))
        {
            await _next(context);
            return;
        }

        var requiredVersion = _agonConfig.MinCliVersion?.Trim();
        if (string.IsNullOrWhiteSpace(requiredVersion))
        {
            await _next(context);
            return;
        }

        if (!TryParseSemver(requiredVersion, out var required))
        {
            _logger.LogWarning(
                "Agon:MinCliVersion is invalid ({MinCliVersion}); skipping enforcement.",
                requiredVersion);
            await _next(context);
            return;
        }

        var currentVersion = context.Request.Headers[CliVersionHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            await WriteUpgradeRequired(context, requiredVersion, null, "Missing CLI version header.");
            return;
        }

        if (!TryParseSemver(currentVersion, out var current))
        {
            await WriteUpgradeRequired(context, requiredVersion, currentVersion, "Invalid CLI version format.");
            return;
        }

        if (CompareSemver(current, required) < 0)
        {
            await WriteUpgradeRequired(context, requiredVersion, currentVersion, "CLI version is below minimum supported version.");
            return;
        }

        await _next(context);
    }

    private bool ShouldBypass(HttpContext context)
    {
        if (_environment.IsEnvironment("Testing"))
        {
            return true;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.Equals("/auth/status", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private async Task WriteUpgradeRequired(
        HttpContext context,
        string requiredVersion,
        string? currentVersion,
        string reason)
    {
        _logger.LogWarning(
            "CLI version check failed. Required={RequiredVersion}, Current={CurrentVersion}, Path={Path}, Reason={Reason}",
            requiredVersion,
            currentVersion ?? "<missing>",
            context.Request.Path,
            reason);

        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Type = "https://httpstatuses.io/426",
            Title = "CLI upgrade required",
            Status = StatusCodes.Status426UpgradeRequired,
            Instance = context.Request.Path,
            Detail = $"Please upgrade Agon CLI to at least v{requiredVersion}."
        };
        problemDetails.Extensions["requiredVersion"] = requiredVersion;
        problemDetails.Extensions["currentVersion"] = currentVersion ?? string.Empty;
        problemDetails.Extensions["installCommand"] = "agon self-update";

        await context.Response.WriteAsJsonAsync(problemDetails);
    }

    private static bool TryParseSemver(string value, out (int Major, int Minor, int Patch) parsed)
    {
        var match = SemverPattern.Match(value);
        if (!match.Success)
        {
            parsed = default;
            return false;
        }

        parsed = (
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));
        return true;
    }

    private static int CompareSemver((int Major, int Minor, int Patch) left, (int Major, int Minor, int Patch) right)
    {
        if (left.Major != right.Major) return left.Major.CompareTo(right.Major);
        if (left.Minor != right.Minor) return left.Minor.CompareTo(right.Minor);
        return left.Patch.CompareTo(right.Patch);
    }
}
