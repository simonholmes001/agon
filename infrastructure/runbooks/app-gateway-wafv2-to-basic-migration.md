# Application Gateway WAF_v2 to Basic Migration Runbook (Issue #58)

## Objective

Replace the current `WAF_v2` gateway with a lower-cost `Basic` tier gateway while preserving:
- App connectivity and routing behavior
- CLI connectivity and command flow
- TLS and redirect expectations

## Target State

- Existing app/data/network architecture remains unchanged.
- Edge is provided by a new parallel Application Gateway `Basic` tier gateway.
- Gateway uses equivalent listeners, probes, redirect, backend host header, and routing rules.
- Cutover is DNS/IP based with explicit rollback to the existing gateway.

## Platform Constraints (Must Validate Before Cutover)

- `Basic v1` is not a valid Application Gateway SKU combination. `Basic` is part of the modern SKU family.
- If `Basic` is not available in your subscription/region, use `Standard_v2` as the fallback lowest-risk path.

Reference: https://learn.microsoft.com/en-us/azure/application-gateway/overview-v2

## IaC Controls Added in This Branch

- `appGatewaySkuName` / `appGatewaySkuTier`: selects Basic/modern or legacy v1 SKU profile.
- `appGatewayInstanceCount`: used only for legacy v1 fixed-capacity SKUs.
- `appGatewayAutoscaleMinCapacity` / `appGatewayAutoscaleMaxCapacity`: used only for `Standard_v2`/`WAF_v2` SKUs.
- `appGatewayResourceSuffix`: allows parallel gateway + public IP creation (for example `basic`) for staged cutover.
- WAF configuration is only emitted when a WAF tier is selected.
- Rule `priority` is only emitted for `Standard_v2`/`WAF_v2` SKUs.

## Phase 1: Discovery and Design Validation

1. Export current gateway shape:
```bash
az network application-gateway show \
  --resource-group rg-agon-dev-frc-app \
  --name agw-agon-dev-frc \
  --output json > /tmp/agw-current.json
```

2. Capture current edge IP:
```bash
az network public-ip show \
  --resource-group rg-agon-dev-frc-app \
  --name pip-agon-dev-frc-agw \
  --query ipAddress \
  --output tsv
```

3. Confirm Basic SKU availability in-region/subscription before change window.

## Phase 2: Build Parallel Gateway

1. Run what-if:
```bash
az deployment sub what-if \
  --location francecentral \
  --name agw-basic-whatif-$(date +%Y%m%d%H%M%S) \
  --template-file infrastructure/bicep/main.bicep \
  --parameters @infrastructure/bicep/parameters/dev.parameters.json \
  --parameters appGatewayResourceSuffix='basic' \
               appGatewaySkuName='Basic' \
               appGatewaySkuTier='Basic'
```

2. Deploy:
```bash
az deployment sub create \
  --location francecentral \
  --name agw-basic-deploy-$(date +%Y%m%d%H%M%S) \
  --template-file infrastructure/bicep/main.bicep \
  --parameters @infrastructure/bicep/parameters/dev.parameters.json \
  --parameters appGatewayResourceSuffix='basic' \
               appGatewaySkuName='Basic' \
               appGatewaySkuTier='Basic'
```

## Phase 3: Validate App and CLI Connectivity

1. Resolve new gateway public IP:
```bash
AGW_BASIC_IP=$(az network public-ip show \
  --resource-group rg-agon-dev-frc-app \
  --name pip-agon-dev-frc-agw-basic \
  --query ipAddress \
  --output tsv)
echo "$AGW_BASIC_IP"
```

2. Validate health and redirect behavior:
```bash
curl -i "http://${AGW_BASIC_IP}/health"
curl -k -i "https://${AGW_BASIC_IP}/health"
```

3. Validate CLI path against the new edge:
```bash
agon config set apiUrl "http://${AGW_BASIC_IP}"
agon sessions
agon status
```

## Phase 4: Cutover, Stabilize, Rollback

1. Cutover:
- Point DNS/client edge target to `pip-agon-dev-frc-agw-basic` IP.
- Keep old gateway running during stabilization.

2. Stabilization window:
- Track App Gateway healthy backend state.
- Watch backend `/health`, API success rate, and CLI command success.
- Monitor 4xx/5xx and latency deltas.

3. Rollback:
- Repoint DNS/client edge target back to `pip-agon-dev-frc-agw` IP.
- Keep both gateways provisioned until traffic and errors normalize.

4. Decommission:
- Remove legacy `WAF_v2` gateway only after stabilization completion and sign-off.

## Cost Tracking

Capture before/after in issue notes:
- Previous gateway SKU and monthly cost estimate
- New gateway SKU and monthly cost estimate
- Observed first billing-cycle delta
