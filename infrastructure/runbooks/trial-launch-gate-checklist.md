# Trial Launch Gate Checklist (20 Tester MVP)

Use this checklist before enabling public tester access.

## 1) Pre-Launch Verification

1. Confirm backend config:
   - `TrialAccess:Enabled=true`
   - `TrialAccess:EnforceEntraGroupMembership=true`
   - `TrialAccess:RequiredEntraGroupObjectIdsCsv` (or `TrialAccess:RequiredEntraGroupObjectIds`) is set to the tester Entra group object ID(s)
   - `TrialAccess:Quota:Enabled=true`
   - `TrialAccess:RequestRateLimit:Enabled=true`
   - `TrialAccess:AdminApiKey` is set and rotated for launch week.
2. Confirm Entra app token configuration emits group membership claims for API tokens.
3. Confirm DB tables are present:
   - `token_usage_records`
   - `trial_audit_events`
   - `trial_controls`
   - `trial_tester_grants` (legacy table; no longer source of truth)
3. Confirm admin API endpoints respond with valid admin key:
   - quota reset
   - kill-switch

## 2) Test Gate (Go/No-Go)

Run the trial-controls integration suite:

```bash
DOTNET_CLI_HOME=/tmp dotnet test backend/tests/Agon.Integration.Tests/Agon.Integration.Tests.csproj \
  --filter "FullyQualifiedName~TrialAccessIntegrationTests" --verbosity minimal
```

Go criteria:

1. Required Entra group membership deny/allow paths pass.
2. Quota and rate-limit deny contracts pass (429 + deterministic payload).
3. Admin reset/kill-switch tests pass.
4. Usage endpoint returns quota + provider/model breakdown.
5. Concurrent same-user request throttling test passes.

No-go criteria:

1. Any failing trial-access integration test.
2. Admin endpoints unauthorized with valid key.
3. Missing deny metadata (`errorCode`, `limitType`, retry/window hints).

## 3) Alert Thresholds (Trial Week)

Track from logs/audit events and metrics:

1. Token burn anomaly:
   - alert if `token_usage_records.total_tokens` for any single user > 2x expected daily budget.
2. Deny spike anomaly:
   - alert if `trial_audit_events` deny events exceed 20/min sustained for 5 minutes.
3. Kill-switch activation:
   - page immediately on `TRIAL_TRAFFIC_DISABLED`.

## 4) Rollback / Disable Procedure

Immediate stop (preferred first action):

```bash
curl -sS -X POST "$AGON_API_BASE/admin/trial/kill-switch" \
  -H "Content-Type: application/json" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -d '{"enabled":true}'
```

Verification:

1. New tester requests return `503` with `TRIAL_TRAFFIC_DISABLED`.
2. `trial_audit_events` contains disable event.

Recovery:

1. Identify root cause (quota misconfig, abuse, infra outage, parser errors).
2. Apply fix and rerun trial integration suite.
3. Re-enable traffic only after green verification:

```bash
curl -sS -X POST "$AGON_API_BASE/admin/trial/kill-switch" \
  -H "Content-Type: application/json" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -d '{"enabled":false}'
```
