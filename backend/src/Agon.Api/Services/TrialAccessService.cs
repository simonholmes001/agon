using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.RateLimiting;
using Agon.Api.Configuration;
using Agon.Application.Interfaces;
using Agon.Application.Models;
using Agon.Infrastructure.Persistence.Entities;
using Agon.Infrastructure.Persistence.PostgreSQL;
using Microsoft.EntityFrameworkCore;

namespace Agon.Api.Services;

public enum TrialAccessOperation
{
    SessionCreate,
    SessionMessage
}

public sealed record TrialAccessResult(
    bool Allowed,
    int StatusCode = StatusCodes.Status200OK,
    string ErrorCode = "",
    string Error = "",
    string? LimitType = null,
    DateTimeOffset? WindowResetAt = null,
    long? RemainingTokens = null,
    int? RetryAfterSeconds = null);

public sealed record TrialUsageSnapshot(
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    long UsedTokens,
    int TokenLimit,
    long RemainingTokens,
    DateTimeOffset? TrialExpiresAt,
    bool TrialActive,
    bool GlobalTrafficEnabled,
    IReadOnlyList<TokenUsageRecord> Records);

public sealed record TrialAdminActionResult(
    bool Success,
    string Message,
    DateTimeOffset? ExpiresAt = null,
    int? AffectedRecords = null,
    bool? GlobalTrafficEnabled = null);

/// <summary>
/// Evaluates and manages invite-only MVP trial controls.
/// </summary>
public sealed class TrialAccessService
{
    private const string GlobalTrafficEnabledKey = "global_traffic_enabled";

    private readonly AgonDbContext _dbContext;
    private readonly ITokenUsageRepository _tokenUsageRepository;
    private readonly TrialAccessConfiguration _config;
    private readonly HashSet<string> _requiredTesterGroupIds;
    private readonly HashSet<string> _adminBypassGroupIds;
    private readonly bool _allowAllAuthenticatedUsers;
    private readonly TrialRequestRateLimiter _rateLimiter;
    private readonly ILogger<TrialAccessService> _logger;

    public TrialAccessService(
        AgonDbContext dbContext,
        ITokenUsageRepository tokenUsageRepository,
        TrialAccessConfiguration config,
        TrialRequestRateLimiter rateLimiter,
        ILogger<TrialAccessService> logger)
    {
        _dbContext = dbContext;
        _tokenUsageRepository = tokenUsageRepository;
        _config = config;
        _requiredTesterGroupIds = ResolveRequiredTesterGroupIds(config);
        _adminBypassGroupIds = ResolveAdminBypassGroupIds(config);
        _allowAllAuthenticatedUsers = ResolveAllowAllAuthenticatedUsers(config.AccessMode, logger);
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public bool IsEnabled => _config.Enabled;

    public bool IsAdminRequestAuthorized(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(_config.AdminApiKey))
        {
            return false;
        }

        if (!request.Headers.TryGetValue("X-Agon-Admin-Key", out var headerValue))
        {
            return false;
        }

        return string.Equals(
            headerValue.ToString().Trim(),
            _config.AdminApiKey.Trim(),
            StringComparison.Ordinal);
    }

    public async Task<TrialAccessResult> EvaluateAsync(
        Guid userId,
        IReadOnlyCollection<string> userGroupIds,
        TrialAccessOperation operation,
        CancellationToken cancellationToken)
    {
        var normalizedUserGroups = NormalizeGroupIds(userGroupIds);
        if (IsAdminBypassGroupMember(normalizedUserGroups))
        {
            await WriteAuditEventAsync(
                action: "trial_access",
                outcome: "allowed",
                userId: userId,
                reasonCode: "TRIAL_ADMIN_BYPASS",
                actor: "system",
                details: new { operation, bypass = "entra-admin-group" },
                cancellationToken);

            return Allow();
        }

        var membershipAccess = EvaluateGroupMembershipAccess(normalizedUserGroups);
        if (!membershipAccess.Allowed)
        {
            await WriteAuditEventAsync(
                action: "trial_access",
                outcome: "denied",
                userId: userId,
                reasonCode: membershipAccess.ErrorCode,
                actor: "system",
                details: new
                {
                    operation,
                    requiredGroups = _requiredTesterGroupIds.Count,
                    accessMode = _config.AccessMode
                },
                cancellationToken);

            return membershipAccess;
        }

        if (!_config.Enabled)
        {
            return Allow();
        }

        var now = DateTimeOffset.UtcNow;

        var trafficEnabled = await IsGlobalTrafficEnabledAsync(cancellationToken);
        if (!trafficEnabled)
        {
            await WriteAuditEventAsync(
                action: "trial_access",
                outcome: "denied",
                userId: userId,
                reasonCode: "TRIAL_TRAFFIC_DISABLED",
                actor: "system",
                details: new { operation },
                cancellationToken);

            return Deny(
                StatusCodes.Status503ServiceUnavailable,
                "TRIAL_TRAFFIC_DISABLED",
                "Trial traffic is temporarily disabled by operators.");
        }

        if (_config.Quota.Enabled)
        {
            var (windowStart, windowEnd) = ResolveQuotaWindow(now);
            var usage = await _tokenUsageRepository.GetWindowSummaryAsync(userId, windowStart, windowEnd, cancellationToken);
            var tokenLimit = Math.Max(1, _config.Quota.TokenLimit);

            if (usage.TotalTokens >= tokenLimit)
            {
                await WriteAuditEventAsync(
                    action: "trial_access",
                    outcome: "denied",
                    userId: userId,
                    reasonCode: "TRIAL_QUOTA_EXCEEDED",
                    actor: "system",
                    details: new
                    {
                        operation,
                        tokenLimit,
                        usedTokens = usage.TotalTokens,
                        windowStart,
                        windowEnd
                    },
                    cancellationToken);

                return Deny(
                    StatusCodes.Status429TooManyRequests,
                    "TRIAL_QUOTA_EXCEEDED",
                    "Token quota exceeded for the active trial window.",
                    limitType: "quota",
                    windowResetAt: windowEnd,
                    remainingTokens: 0);
            }
        }

        if (_config.RequestRateLimit.Enabled)
        {
            var lease = _rateLimiter.TryAcquire(userId);
            if (!lease.Allowed)
            {
                await WriteAuditEventAsync(
                    action: "trial_access",
                    outcome: "denied",
                    userId: userId,
                    reasonCode: "TRIAL_RATE_LIMIT_EXCEEDED",
                    actor: "system",
                    details: new { operation, lease.RetryAfterSeconds },
                    cancellationToken);

                return Deny(
                    StatusCodes.Status429TooManyRequests,
                    "TRIAL_RATE_LIMIT_EXCEEDED",
                    "Too many requests for this user. Retry later.",
                    limitType: "rate",
                    retryAfterSeconds: lease.RetryAfterSeconds);
            }
        }

        await WriteAuditEventAsync(
            action: "trial_access",
            outcome: "allowed",
            userId: userId,
            reasonCode: "TRIAL_ALLOWED",
            actor: "system",
            details: new { operation },
            cancellationToken);

        return Allow();
    }

    public async Task<TrialAccessResult> EvaluateUsageAccessAsync(
        Guid userId,
        IReadOnlyCollection<string> userGroupIds,
        CancellationToken cancellationToken)
    {
        var normalizedUserGroups = NormalizeGroupIds(userGroupIds);
        if (IsAdminBypassGroupMember(normalizedUserGroups))
        {
            return Allow();
        }

        var access = EvaluateGroupMembershipAccess(normalizedUserGroups);
        if (!access.Allowed)
        {
            await WriteAuditEventAsync(
                action: "trial_usage_access",
                outcome: "denied",
                userId: userId,
                reasonCode: access.ErrorCode,
                actor: "system",
                details: new
                {
                    requiredGroups = _requiredTesterGroupIds.Count,
                    accessMode = _config.AccessMode
                },
                cancellationToken);
        }

        return access;
    }

    public async Task<TrialUsageSnapshot> GetUsageSnapshotAsync(
        Guid userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var (windowStart, windowEnd) = from.HasValue && to.HasValue
            ? (from.Value, to.Value)
            : ResolveQuotaWindow(now);

        if (windowEnd <= windowStart)
        {
            windowEnd = windowStart.AddDays(Math.Max(1, _config.Quota.WindowDays));
        }

        var summary = await _tokenUsageRepository.GetWindowSummaryAsync(userId, windowStart, windowEnd, cancellationToken);
        var records = await _tokenUsageRepository.ListByUserAndWindowAsync(userId, windowStart, windowEnd, cancellationToken);

        var tokenLimit = Math.Max(1, _config.Quota.TokenLimit);
        var remaining = Math.Max(0, tokenLimit - summary.TotalTokens);
        var trafficEnabled = await IsGlobalTrafficEnabledAsync(cancellationToken);

        return new TrialUsageSnapshot(
            windowStart,
            windowEnd,
            summary.TotalTokens,
            tokenLimit,
            remaining,
            TrialExpiresAt: null,
            TrialActive: _config.Enabled,
            trafficEnabled,
            records);
    }

    public async Task<TrialAdminActionResult> GrantTesterAsync(
        Guid userId,
        string actor,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var effectiveExpiry = expiresAt ?? now.AddDays(Math.Max(1, _config.DefaultTrialDays));

        var entity = await _dbContext.TrialTesterGrants
            .FirstOrDefaultAsync(grant => grant.UserId == userId, cancellationToken);

        if (entity is null)
        {
            entity = new TrialTesterGrantEntity
            {
                UserId = userId,
                GrantedAt = now,
                GrantedBy = actor,
                ExpiresAt = effectiveExpiry,
                UpdatedAt = now
            };
            await _dbContext.TrialTesterGrants.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity.ExpiresAt = effectiveExpiry;
            entity.RevokedAt = null;
            entity.RevokedBy = null;
            entity.RevokeReason = null;
            entity.GrantedBy = actor;
            entity.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditEventAsync(
            action: "admin_grant_tester",
            outcome: "success",
            userId: userId,
            reasonCode: "TRIAL_TESTER_GRANTED",
            actor: actor,
            details: new { expiresAt = effectiveExpiry },
            cancellationToken);

        _logger.LogInformation("Granted tester access for user {UserId} until {Expiry} by {Actor}", userId, effectiveExpiry, actor);

        return new TrialAdminActionResult(true, "Tester access granted.", ExpiresAt: effectiveExpiry);
    }

    public async Task<TrialAdminActionResult> RevokeTesterAsync(
        Guid userId,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.TrialTesterGrants
            .FirstOrDefaultAsync(grant => grant.UserId == userId, cancellationToken);

        if (entity is null)
        {
            return new TrialAdminActionResult(false, "Tester grant not found.");
        }

        entity.RevokedAt = DateTimeOffset.UtcNow;
        entity.RevokedBy = actor;
        entity.RevokeReason = reason?.Trim();
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditEventAsync(
            action: "admin_revoke_tester",
            outcome: "success",
            userId: userId,
            reasonCode: "TRIAL_TESTER_REVOKED",
            actor: actor,
            details: new { reason = entity.RevokeReason },
            cancellationToken);

        _logger.LogInformation("Revoked tester access for user {UserId} by {Actor}", userId, actor);

        return new TrialAdminActionResult(true, "Tester access revoked.");
    }

    public async Task<TrialAdminActionResult> ResetQuotaAsync(
        Guid userId,
        string actor,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var (windowStart, windowEnd) = ResolveQuotaWindow(now);

        var deleted = await _tokenUsageRepository.DeleteByUserAndWindowAsync(userId, windowStart, windowEnd, cancellationToken);

        await WriteAuditEventAsync(
            action: "admin_reset_quota",
            outcome: "success",
            userId: userId,
            reasonCode: "TRIAL_QUOTA_RESET",
            actor: actor,
            details: new { windowStart, windowEnd, deleted },
            cancellationToken);

        _logger.LogInformation(
            "Reset quota usage for user {UserId}. Deleted {DeletedRecords} records in window {WindowStart} - {WindowEnd} by {Actor}",
            userId,
            deleted,
            windowStart,
            windowEnd,
            actor);

        return new TrialAdminActionResult(true, "Quota usage reset.", AffectedRecords: deleted);
    }

    public async Task<TrialAdminActionResult> SetGlobalTrafficEnabledAsync(
        bool enabled,
        string actor,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.TrialControls
            .FirstOrDefaultAsync(control => control.Name == GlobalTrafficEnabledKey, cancellationToken);

        if (entity is null)
        {
            entity = new TrialControlEntity
            {
                Name = GlobalTrafficEnabledKey,
                Value = enabled ? "true" : "false",
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _dbContext.TrialControls.AddAsync(entity, cancellationToken);
        }
        else
        {
            entity.Value = enabled ? "true" : "false";
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await WriteAuditEventAsync(
            action: "admin_set_kill_switch",
            outcome: "success",
            userId: null,
            reasonCode: enabled ? "TRIAL_TRAFFIC_ENABLED" : "TRIAL_TRAFFIC_DISABLED",
            actor: actor,
            details: new { enabled },
            cancellationToken);

        _logger.LogWarning("Trial global traffic set to {Enabled} by {Actor}", enabled, actor);

        return new TrialAdminActionResult(true, "Global trial traffic updated.", GlobalTrafficEnabled: enabled);
    }

    public async Task<bool> IsGlobalTrafficEnabledAsync(CancellationToken cancellationToken)
    {
        var control = await _dbContext.TrialControls
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Name == GlobalTrafficEnabledKey, cancellationToken);

        if (control is null)
        {
            return true;
        }

        return bool.TryParse(control.Value, out var value) ? value : true;
    }

    private static TrialAccessResult Allow() => new(true);

    private TrialAccessResult EvaluateGroupMembershipAccess(HashSet<string> normalizedUserGroups)
    {
        if (!_config.Enabled)
        {
            return Allow();
        }

        if (_allowAllAuthenticatedUsers || !_config.EnforceEntraGroupMembership)
        {
            return Allow();
        }

        if (_requiredTesterGroupIds.Count == 0)
        {
            _logger.LogError(
                "TrialAccess is enabled with EnforceEntraGroupMembership=true but no required group IDs are configured.");
            return Deny(
                StatusCodes.Status503ServiceUnavailable,
                "TRIAL_GROUP_CONFIG_INVALID",
                "Trial access group configuration is invalid.");
        }

        if (normalizedUserGroups.Overlaps(_requiredTesterGroupIds))
        {
            return Allow();
        }

        return Deny(
            StatusCodes.Status403Forbidden,
            "TRIAL_NOT_ALLOWLISTED",
            "User is not approved for MVP trial access.");
    }

    private bool IsAdminBypassGroupMember(HashSet<string> normalizedUserGroups)
    {
        if (!_config.Enabled || _adminBypassGroupIds.Count == 0)
        {
            return false;
        }

        return normalizedUserGroups.Overlaps(_adminBypassGroupIds);
    }

    private static TrialAccessResult Deny(
        int statusCode,
        string errorCode,
        string error,
        string? limitType = null,
        DateTimeOffset? windowResetAt = null,
        long? remainingTokens = null,
        int? retryAfterSeconds = null)
    {
        return new TrialAccessResult(
            Allowed: false,
            StatusCode: statusCode,
            ErrorCode: errorCode,
            Error: error,
            LimitType: limitType,
            WindowResetAt: windowResetAt,
            RemainingTokens: remainingTokens,
            RetryAfterSeconds: retryAfterSeconds);
    }

    private (DateTimeOffset Start, DateTimeOffset End) ResolveQuotaWindow(DateTimeOffset now)
    {
        var windowDays = Math.Max(1, _config.Quota.WindowDays);
        var epoch = DateTimeOffset.UnixEpoch;
        var utcDateStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var elapsedDays = (long)Math.Floor((utcDateStart - epoch).TotalDays);
        var bucket = elapsedDays / windowDays;
        var start = epoch.AddDays(bucket * windowDays);
        var end = start.AddDays(windowDays);
        return (start, end);
    }

    private async Task WriteAuditEventAsync(
        string action,
        string outcome,
        Guid? userId,
        string reasonCode,
        string actor,
        object? details,
        CancellationToken cancellationToken)
    {
        var entity = new TrialAuditEventEntity
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = outcome,
            UserId = userId,
            Actor = actor,
            ReasonCode = reasonCode,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            OccurredAt = DateTimeOffset.UtcNow
        };

        await _dbContext.TrialAuditEvents.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static HashSet<string> ResolveRequiredTesterGroupIds(TrialAccessConfiguration config)
    {
        var configuredIds = new List<string>();
        configuredIds.AddRange(config.RequiredEntraGroupObjectIds);

        if (!string.IsNullOrWhiteSpace(config.RequiredEntraGroupObjectIdsCsv))
        {
            configuredIds.AddRange(config.RequiredEntraGroupObjectIdsCsv.Split(',', StringSplitOptions.TrimEntries));
        }

        return NormalizeGroupIds(configuredIds);
    }

    private static HashSet<string> ResolveAdminBypassGroupIds(TrialAccessConfiguration config)
    {
        var configuredIds = new List<string>();
        configuredIds.AddRange(config.AdminBypassEntraGroupObjectIds);

        if (!string.IsNullOrWhiteSpace(config.AdminBypassEntraGroupObjectIdsCsv))
        {
            configuredIds.AddRange(config.AdminBypassEntraGroupObjectIdsCsv.Split(',', StringSplitOptions.TrimEntries));
        }

        return NormalizeGroupIds(configuredIds);
    }

    private static bool ResolveAllowAllAuthenticatedUsers(string? configuredAccessMode, ILogger<TrialAccessService> logger)
    {
        if (string.IsNullOrWhiteSpace(configuredAccessMode)
            || string.Equals(configuredAccessMode, TrialAccessModes.RestrictedGroups, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(configuredAccessMode, TrialAccessModes.AllAuthenticatedUsers, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        logger.LogWarning(
            "Unknown TrialAccess access mode '{AccessMode}'. Falling back to '{DefaultMode}'.",
            configuredAccessMode,
            TrialAccessModes.RestrictedGroups);
        return false;
    }

    private static HashSet<string> NormalizeGroupIds(IEnumerable<string> groupIds)
    {
        return groupIds
            .Where(groupId => !string.IsNullOrWhiteSpace(groupId))
            .Select(groupId => groupId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class TrialRequestRateLimiter
{
    private readonly TrialAccessConfiguration _config;
    private readonly ConcurrentDictionary<Guid, TokenBucketRateLimiter> _limiters = new();

    public TrialRequestRateLimiter(TrialAccessConfiguration config)
    {
        _config = config;
    }

    public TrialRateLimitLease TryAcquire(Guid userId)
    {
        if (!_config.Enabled || !_config.RequestRateLimit.Enabled)
        {
            return TrialRateLimitLease.AllowedLease();
        }

        var limiter = _limiters.GetOrAdd(userId, _ => BuildLimiter());
        var lease = limiter.AttemptAcquire(1);
        if (lease.IsAcquired)
        {
            return TrialRateLimitLease.AllowedLease();
        }

        var retryAfterSeconds = 0;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) && retryAfter > TimeSpan.Zero)
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }

        return TrialRateLimitLease.DeniedLease(retryAfterSeconds);
    }

    private TokenBucketRateLimiter BuildLimiter()
    {
        var requestsPerMinute = Math.Max(1, _config.RequestRateLimit.RequestsPerMinute);
        var burst = Math.Max(1, _config.RequestRateLimit.BurstCapacity);

        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = burst,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = requestsPerMinute,
            AutoReplenishment = true
        });
    }
}

public sealed record TrialRateLimitLease(bool Allowed, int RetryAfterSeconds)
{
    public static TrialRateLimitLease AllowedLease() => new(true, 0);

    public static TrialRateLimitLease DeniedLease(int retryAfterSeconds) => new(false, Math.Max(0, retryAfterSeconds));
}
