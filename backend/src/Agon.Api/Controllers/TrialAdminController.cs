using System.Security.Claims;
using Agon.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Agon.Api.Controllers;

[ApiController]
[Route("admin/trial")]
public sealed class TrialAdminController : ControllerBase
{
    private readonly TrialAccessService _trialAccessService;

    public TrialAdminController(TrialAccessService trialAccessService)
    {
        _trialAccessService = trialAccessService;
    }

    [HttpPut("testers/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GrantTester(
        [FromRoute] Guid userId,
        [FromBody] GrantTesterRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsAdminAuthorized())
        {
            return Unauthorized(new { error = "Admin credentials required." });
        }

        var result = await _trialAccessService.GrantTesterAsync(
            userId,
            ResolveActor(),
            request?.ExpiresAtUtc,
            cancellationToken);

        return Ok(new { message = result.Message, expiresAt = result.ExpiresAt });
    }

    [HttpDelete("testers/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeTester(
        [FromRoute] Guid userId,
        [FromBody] RevokeTesterRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsAdminAuthorized())
        {
            return Unauthorized(new { error = "Admin credentials required." });
        }

        var result = await _trialAccessService.RevokeTesterAsync(
            userId,
            ResolveActor(),
            request?.Reason,
            cancellationToken);

        if (!result.Success)
        {
            return NotFound(new { error = result.Message });
        }

        return Ok(new { message = result.Message });
    }

    [HttpPost("quotas/{userId:guid}/reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetQuota(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        if (!IsAdminAuthorized())
        {
            return Unauthorized(new { error = "Admin credentials required." });
        }

        var result = await _trialAccessService.ResetQuotaAsync(
            userId,
            ResolveActor(),
            cancellationToken);

        return Ok(new { message = result.Message, affectedRecords = result.AffectedRecords ?? 0 });
    }

    [HttpPost("kill-switch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetKillSwitch(
        [FromBody] SetKillSwitchRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAdminAuthorized())
        {
            return Unauthorized(new { error = "Admin credentials required." });
        }

        var result = await _trialAccessService.SetGlobalTrafficEnabledAsync(
            enabled: !request.Enabled,
            actor: ResolveActor(),
            cancellationToken: cancellationToken);

        return Ok(new
        {
            message = result.Message,
            killSwitchEnabled = request.Enabled,
            globalTrafficEnabled = result.GlobalTrafficEnabled
        });
    }

    private bool IsAdminAuthorized() => _trialAccessService.IsAdminRequestAuthorized(Request);

    private string ResolveActor()
    {
        var claimActor =
            User.FindFirstValue("oid")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return string.IsNullOrWhiteSpace(claimActor) ? "admin-api-key" : claimActor;
    }
}

public sealed record GrantTesterRequest(DateTimeOffset? ExpiresAtUtc);

public sealed record RevokeTesterRequest(string? Reason);

public sealed record SetKillSwitchRequest(bool Enabled);
