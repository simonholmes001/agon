using System.Globalization;
using System.Security.Claims;
using Agon.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Agon.Api.Controllers;

[ApiController]
[Route("usage")]
public sealed class UsageController : ControllerBase
{
    private const string EntraGroupsClaimType = "groups";
    private const string LegacyGroupsClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/groups";

    private readonly TrialAccessService _trialAccessService;

    public UsageController(TrialAccessService trialAccessService)
    {
        _trialAccessService = trialAccessService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsage(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        var userId = ResolveCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { error = "Authenticated user identity is required." });
        }

        var parsedFrom = ParseUtcDateTimeOffset(from);
        var parsedTo = ParseUtcDateTimeOffset(to);
        var access = await _trialAccessService.EvaluateUsageAccessAsync(
            userId,
            ResolveCurrentUserGroupIds(),
            cancellationToken);
        if (!access.Allowed)
        {
            return StatusCode(access.StatusCode, new
            {
                errorCode = access.ErrorCode,
                error = access.Error
            });
        }

        var snapshot = await _trialAccessService.GetUsageSnapshotAsync(
            userId,
            parsedFrom,
            parsedTo,
            cancellationToken);

        var providerModelTotals = snapshot.Records
            .GroupBy(record => new { record.Provider, record.Model })
            .Select(group => new
            {
                provider = group.Key.Provider,
                model = group.Key.Model,
                totalTokens = group.Sum(record => record.TotalTokens),
                promptTokens = group.Sum(record => record.PromptTokens),
                completionTokens = group.Sum(record => record.CompletionTokens)
            })
            .OrderByDescending(item => item.totalTokens)
            .ToList();

        return Ok(new
        {
            trialEnabled = _trialAccessService.IsEnabled,
            windowStart = snapshot.WindowStart,
            windowEnd = snapshot.WindowEnd,
            quota = new
            {
                tokenLimit = snapshot.TokenLimit,
                usedTokens = snapshot.UsedTokens,
                remainingTokens = snapshot.RemainingTokens
            },
            trial = new
            {
                isActive = snapshot.TrialActive,
                expiresAt = snapshot.TrialExpiresAt,
                globalTrafficEnabled = snapshot.GlobalTrafficEnabled
            },
            usageByProviderModel = providerModelTotals
        });
    }

    private Guid ResolveCurrentUserId()
    {
        var claimValue =
            User.FindFirstValue("oid")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(claimValue) && Guid.TryParse(claimValue, out var parsedGuid))
        {
            return parsedGuid;
        }

        return Guid.Empty;
    }

    private IReadOnlyCollection<string> ResolveCurrentUserGroupIds()
    {
        return User.Claims
            .Where(claim =>
                string.Equals(claim.Type, EntraGroupsClaimType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim.Type, LegacyGroupsClaimType, StringComparison.OrdinalIgnoreCase))
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParseUtcDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
