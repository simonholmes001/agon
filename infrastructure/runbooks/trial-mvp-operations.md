# MVP Trial Operations Runbook

Use this runbook to operate the invite-only tester cohort during MVP trial week.

## Scope

- Environment: API deployment where `TrialAccess:Enabled=true`
- Audience: on-call operator with admin API key
- Objective: use Entra group membership as tester source-of-truth, plus quota reset and kill-switch controls

## Prerequisites

1. Set API base URL:
```bash
export AGON_API_BASE="https://<api-host>"
```
2. Set admin key:
```bash
export AGON_ADMIN_KEY="<trial-admin-key>"
```
3. Set tester Entra group object ID:
```bash
export AGON_TESTER_GROUP_OBJECT_ID="<entra-group-object-id>"
```

All admin requests require header:

```bash
-H "X-Agon-Admin-Key: $AGON_ADMIN_KEY"
```

## Manage Tester Access (Entra Source of Truth)

Recommended one-shot onboarding command (no provider key sharing):

```bash
./infrastructure/scripts/onboard-tester.sh \
  --user-upn "user@domain.com" \
  --group "$AGON_TESTER_GROUP_OBJECT_ID"
```

Dry-run validation before granting access:

```bash
./infrastructure/scripts/onboard-tester.sh \
  --user-upn "user@domain.com" \
  --group "$AGON_TESTER_GROUP_OBJECT_ID" \
  --dry-run
```

The script validates that provider keys are server-managed via Key Vault references before onboarding the tester.

Add user to tester group:

```bash
az ad group member add \
  --group "$AGON_TESTER_GROUP_OBJECT_ID" \
  --member-id "<user-object-id-guid>"
```

Remove user from tester group:

```bash
az ad group member remove \
  --group "$AGON_TESTER_GROUP_OBJECT_ID" \
  --member-id "<user-object-id-guid>"
```

List current tester members:

```bash
az ad group member list \
  --group "$AGON_TESTER_GROUP_OBJECT_ID" \
  --query "[].{displayName:displayName,id:id,userPrincipalName:userPrincipalName}" -o table
```

Legacy note: `/admin/trial/testers/*` endpoints are no longer authoritative for access gating.

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
   - `TRIAL_NOT_ALLOWLISTED` (user not in required Entra group)
   - `TRIAL_QUOTA_RESET`
   - `TRIAL_TRAFFIC_DISABLED` / `TRIAL_TRAFFIC_ENABLED`

## Incident Guidance

1. Cost spike: enable kill-switch first, then investigate usage records by user.
2. Single user abuse: remove user from Entra tester group and reset quota only if reinstating.
3. Recovery: re-enable kill-switch only after confirming normal request and token rates.
