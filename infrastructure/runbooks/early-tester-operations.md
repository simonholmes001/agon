# Early Tester Operations (Entra Group Source of Truth)

Use this runbook to onboard and operate early testers when Entra group membership is the access source of truth.

## Security Model (No Key Sharing)

- Testers never receive provider keys (`OPENAI_KEY`, `CLAUDE_KEY`, `GEMINI_KEY`, `DEEPSEEK_KEY`).
- Provider keys stay server-side in Key Vault and are injected into App Service via Key Vault references.
- Tester onboarding is done only by Entra group membership + `agon login`.

## 1) One-Time Setup (Automated via IaC)

These settings should be deployed through Bicep (not set manually in the portal):

```text
Authentication__Enabled=true
TrialAccess__Enabled=true
TrialAccess__EnforceEntraGroupMembership=true
TrialAccess__RequiredEntraGroupObjectIdsCsv=<AGON_EARLY_USERS_GROUP_OBJECT_ID>
TrialAccess__Quota__Enabled=true
TrialAccess__Quota__TokenLimit=40000
TrialAccess__Quota__WindowDays=7
TrialAccess__RequestRateLimit__Enabled=true
TrialAccess__RequestRateLimit__RequestsPerMinute=20
TrialAccess__RequestRateLimit__BurstCapacity=10
```

In this repo, these are wired via Bicep parameters:

- `trialAccessEnabled`
- `trialEnforceEntraGroupMembership`
- `trialRequiredEntraGroupObjectIdsCsv`
- `trialQuotaEnabled`
- `trialQuotaTokenLimit`
- `trialQuotaWindowDays`
- `trialRequestRateLimitEnabled`
- `trialRequestsPerMinute`
- `trialBurstCapacity`

Implemented for `dev` in:

- `infrastructure/bicep/parameters/dev.parameters.json`

Deploy (no manual portal edits):

```bash
az deployment sub create \
  --location swedencentral \
  --template-file infrastructure/bicep/main.bicep \
  --parameters @infrastructure/bicep/parameters/dev.parameters.json
```

## 2) Onboard One New Tester

Preferred path (one-shot script):

```bash
./infrastructure/scripts/onboard-tester.sh \
  --user-upn "user@domain.com" \
  --group "agon-early-users"
```

Dry-run validation (no membership change):

```bash
./infrastructure/scripts/onboard-tester.sh \
  --user-upn "user@domain.com" \
  --group "agon-early-users" \
  --dry-run
```

What the script validates before onboarding:

- Provider keys are configured as Key Vault references in App Service (not plain values).
- Trial access settings exist for group-based gating.
- Target user/group resolves in Entra ID.

Manual path (if needed):

1. Get group object ID:

```bash
az ad group show --group "agon-early-users" --query id -o tsv
```

2. Get user object ID:

```bash
az ad user show --id "user@domain.com" --query id -o tsv
```

3. Add user to group:

```bash
az ad group member add --group "<group-object-id>" --member-id "<user-object-id>"
```

4. User runs `agon login`, then uses Agon normally.

## 3) How Limits Apply to That User

- Limits are global settings, but enforced per user automatically.
- You do not set a custom token cap per user today.
- This user gets the same quota/rate policy as other testers.

## 4) Per-User Operational Controls

1. Remove user access immediately:

```bash
az ad group member remove --group "<group-object-id>" --member-id "<user-object-id>"
```

2. Reset that user’s consumed quota:

```bash
curl -X POST "$AGON_API_BASE/admin/trial/quotas/<user-object-id>/reset" \
  -H "Authorization: Bearer $ADMIN_BEARER" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{}'
```

3. Kill switch all tester traffic:

```bash
curl -X POST "$AGON_API_BASE/admin/trial/kill-switch" \
  -H "Authorization: Bearer $ADMIN_BEARER" \
  -H "X-Agon-Admin-Key: $AGON_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"enabled":true}'
```

## 5) Verify

1. Tester quota view:

```bash
curl "$AGON_API_BASE/usage" -H "Authorization: Bearer <tester-token>"
```

2. If denied with `TRIAL_NOT_ALLOWLISTED`, check:

- User is in `agon-early-users`
- Token includes correct `groups` claim
- Backend `TrialAccess__RequiredEntraGroupObjectIdsCsv` matches group object ID exactly

## Should These Trial Settings Be Removed Later?

No code removal is required.

- Keep the code and IaC parameters in place.
- To turn trial behavior off, set `TrialAccess__Enabled=false` in environment configuration.
- For production after trial, you can switch policies/values, but the settings should remain versioned in IaC.
