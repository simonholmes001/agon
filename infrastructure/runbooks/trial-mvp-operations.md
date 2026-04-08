# MVP Trial Operations Runbook

Use this runbook to operate the invite-only tester cohort during MVP trial week.

## Scope

- Environment: API deployment where `TrialAccess:Enabled=true`
- Audience: on-call operator with admin API key
- Objective: grant/revoke testers, reset quota, and use kill-switch safely

## Prerequisites

1. Set API base URL:
```bash
export AGON_API_BASE="https://<api-host>"
```
2. Set admin key:
```bash
export AGON_ADMIN_KEY="<trial-admin-key>"
```

All admin requests require header:

```bash
-H "X-Agon-Admin-Key: $AGON_ADMIN_KEY"
```

## Grant Tester Access

Grant for default trial duration:

```bash
curl -sS -X PUT "$AGON_API_BASE/admin/trial/testers/<user-guid>" \
  -H "Content-Type: application/json" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -d '{}'
```

Grant with explicit expiry:

```bash
curl -sS -X PUT "$AGON_API_BASE/admin/trial/testers/<user-guid>" \
  -H "Content-Type: application/json" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -d '{"expiresAtUtc":"2026-04-30T23:59:59Z"}'
```

## Revoke Tester Access

```bash
curl -sS -X DELETE "$AGON_API_BASE/admin/trial/testers/<user-guid>" \
  -H "Content-Type: application/json" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -d '{"reason":"abuse or offboarding"}'
```

## Reset Tester Quota

```bash
curl -sS -X POST "$AGON_API_BASE/admin/trial/quotas/<user-guid>/reset" \
  -H "Content-Type: application/json" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -d '{}'
```

## Emergency Kill-Switch

Disable all tester traffic immediately:

```bash
curl -sS -X POST "$AGON_API_BASE/admin/trial/kill-switch" \
  -H "Content-Type: application/json" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -d '{"enabled":true}'
```

Re-enable tester traffic:

```bash
curl -sS -X POST "$AGON_API_BASE/admin/trial/kill-switch" \
  -H "Content-Type: application/json" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -d '{"enabled":false}'
```

## Verification Checks

1. Confirm tester access:
```bash
curl -sS "$AGON_API_BASE/usage" -H "Authorization: Bearer <tester-token>"
```
2. Confirm kill-switch effect (expect `503` + `TRIAL_TRAFFIC_DISABLED` when enabled).
3. Confirm audit trail in DB table `trial_audit_events` for:
   - `TRIAL_TESTER_GRANTED`
   - `TRIAL_TESTER_REVOKED`
   - `TRIAL_QUOTA_RESET`
   - `TRIAL_TRAFFIC_DISABLED` / `TRIAL_TRAFFIC_ENABLED`

## Incident Guidance

1. Cost spike: enable kill-switch first, then investigate usage records by user.
2. Single user abuse: revoke user and reset quota only if reinstating.
3. Recovery: re-enable kill-switch only after confirming normal request and token rates.
