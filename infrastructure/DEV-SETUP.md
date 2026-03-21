# Dev Infrastructure Setup (Azure + Bicep + GitHub OIDC)

This document is the source of truth for the `dev` backend infrastructure deployment.

## Target Architecture (Dev)

- Public edge: Azure Application Gateway (SKU is parameterized; default is `Basic` tier for cost reduction)
- App tier: Azure App Service (Linux) in a dedicated app resource group
- Data tier: Azure Database for PostgreSQL Flexible Server + Azure Cache for Redis + Key Vault
- Network tier: dedicated VNet/subnets/private DNS zones (including App Service private link DNS)
- Telemetry: Log Analytics + Application Insights + metric alert action group
- IaC: Bicep only
- CI/CD auth: GitHub OIDC (no client secret)

## Resource Group Strategy

Deployment is **subscription-scope** and creates three resource groups:

- `rg-agon-dev-swc-net`: VNet, subnets, private DNS zones
- `rg-agon-dev-swc-app`: App Service, Application Gateway, monitoring + alerts
- `rg-agon-dev-swc-data`: PostgreSQL, Redis, Key Vault (+ private endpoints)

This split follows Azure operational best practice: isolate lifecycle and access by domain (network/app/data), not by single mega-RG.

## Naming Standard

Prefix used by this project: `agon-dev-swc`

Naming follows Azure CAF guidance:

- https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-naming?toc=%2Fazure%2Fazure-resource-manager%2Fmanagement%2Ftoc.json#example-azure-resource-names
- https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations
- https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules

## Environment Values (Current)

- Subscription ID: `<your-subscription-id>`
- Tenant ID: `<your-tenant-id>`
- Region: `swedencentral` (or your approved region)
- Prefix: `agon-dev-swc` (or your approved prefix)
- Alert email: `<your-alert-email>`
- `appGatewaySubnetPrefix`: `10.42.4.0/24` (default in dev)
- `appGatewayResourceSuffix`: `` (empty for primary gateway names)
- `appGatewaySkuName`: `Basic` (default in this branch)
- `appGatewaySkuTier`: `Basic` (default in this branch)
- `appGatewayPublicHostName`: optional but recommended (for HTTPS listener + host validation)
- `appGatewaySslPolicyName`: `AppGwSslPolicy20220101S` (default)

## One-Time Azure Identity Setup (OIDC)

### 1) Create App Registration

1. Azure Portal -> `Microsoft Entra ID` -> `App registrations` -> `New registration`
2. Name: `agon-gha-dev-deployer`
3. Accounts: single tenant
4. Register

Capture:

- `Application (client) ID` -> GitHub secret `AZURE_CLIENT_ID`
- `Directory (tenant) ID` -> GitHub secret `AZURE_TENANT_ID`

### 2) Verify Enterprise Application (Service Principal)

1. Azure Portal -> `Microsoft Entra ID` -> `Enterprise applications`
2. Open `agon-gha-dev-deployer`
3. Confirm:
   - App ID matches the registration
   - Object ID is present (used in RBAC troubleshooting)

### 3) Add Federated Credentials (exact names)

On App Registration -> `Certificates & secrets` -> `Federated credentials`:

1. Name: `github-pr-validate`
   - Scenario: GitHub Actions deploying Azure resources
   - Organization: `simonholmes001`
   - Repository: `agon`
   - Entity type: `Pull request`
   - Subject: `repo:simonholmes001/agon:pull_request`

2. Name: `github-main-deploy`
   - Scenario: GitHub Actions deploying Azure resources
   - Organization: `simonholmes001`
   - Repository: `agon`
   - Entity type: `Branch`
   - Branch: `main`
   - Subject: `repo:simonholmes001/agon:ref:refs/heads/main`

Audience for both:

- `api://AzureADTokenExchange`

### 4) Assign RBAC on Subscription

Because templates run with `az deployment sub ...`, assign role at subscription scope.

Assign the service principal (`agon-gha-dev-deployer`) one of:

- `Owner` (simplest for dev), or
- `Contributor` + `User Access Administrator`

`Contributor` alone is insufficient because deployment creates Key Vault RBAC role assignments.

### 5) Wait for Propagation

Allow 5-10 minutes after federated credential/RBAC changes before rerunning workflows.

## GitHub Secrets Required

Required:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_POSTGRES_ADMIN_PASSWORD`

Required for backend container deployment (Docker Hub):

- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN`
- `DOCKERHUB_IMAGE_NAME` (example: `yourdockerhubuser/agon-backend`)

Optional (written into Key Vault during deploy):

- `OPENAI_KEY`
- `CLAUDE_KEY`
- `GEMINI_KEY`
- `DEEPSEEK_KEY`

Optional (required for HTTPS edge listener rollout):

- `APP_GATEWAY_SSL_CERTIFICATE_PFX_BASE64` (base64-encoded PFX for listener cert)
- `APP_GATEWAY_SSL_CERTIFICATE_PASSWORD`
- `AGON_API_HOSTNAME` (public API FQDN bound to the certificate, for example `api-dev.example.com`)
- `APP_GATEWAY_SSL_POLICY_NAME` (optional override; defaults to `AppGwSslPolicy20220101S`)

## Where to Find IDs in Azure Portal

- Client ID: `Entra ID -> App registrations -> <app> -> Overview -> Application (client) ID`
- Tenant ID: `Entra ID -> App registrations -> <app> -> Overview -> Directory (tenant) ID`
- SP object ID: `Entra ID -> Enterprise applications -> <app> -> Overview -> Object ID`

## Workflows

- `.github/workflows/infrastructure-validate.yaml`
  - Trigger: PR to `main` (infra paths)
  - Runs Bicep build + subscription `what-if`

- `.github/workflows/infrastructure-deploy-dev.yaml`
  - Trigger: push/merge to `main` (infra paths), or manual dispatch
  - Runs subscription deployment to create/update `dev` infrastructure

- `.github/workflows/backend-deploy-dev.yaml`
  - Trigger: push/merge to `main` (backend paths), or manual dispatch
  - Runs backend tests, builds and pushes Docker image to Docker Hub, deploys container to App Service, verifies `/health` through Application Gateway
  - Health check prefers `https://$AGON_API_HOSTNAME/health` when hostname is configured, otherwise falls back to legacy HTTP IP check
  - Also triggers when App Service edge IaC changes (`main.bicep` / `app-edge-dev.bicep`) so container runtime settings are reapplied automatically

## Legacy Front Door Cleanup (if previously deployed)

If older runs created Front Door/CDN resources, remove them manually from `rg-agon-dev-swc-app`:

```bash
az resource delete \
  --resource-group rg-agon-dev-swc-app \
  --resource-type Microsoft.Cdn/profiles \
  --name afd-agon-dev-swc

az resource delete \
  --resource-group rg-agon-dev-swc-app \
  --resource-type Microsoft.Cdn/cdnWebApplicationFirewallPolicies \
  --name waf-agon-dev-swc
```

Then rerun deployment from `main`.

## Execution Flow

1. Open PR with infra changes -> validation workflow runs (`build` + `what-if`)
2. Merge PR to `main` -> deploy workflow runs (`az deployment sub create`)
3. Verify resources in Azure:
   - `rg-agon-dev-swc-net`
   - `rg-agon-dev-swc-app`
   - `rg-agon-dev-swc-data`

## Common Errors

### `AADSTS700213` (no matching federated identity)

Subject mismatch in federated credential.

Use exact subjects:

- `repo:simonholmes001/agon:pull_request`
- `repo:simonholmes001/agon:ref:refs/heads/main`

### `No subscriptions found for <client-id>`

Service principal has no valid RBAC on subscription (or wrong IDs in secrets).

### `roleAssignments/write` denied

Add `User Access Administrator` (or `Owner`) in addition to `Contributor`.

### `FlagMustBeSetForRestore` on Document Intelligence account

Cause: a previously deleted `docint-*` account still exists in soft-deleted state.

Check/purge:

```bash
az cognitiveservices account list-deleted -o table
az cognitiveservices account purge \
  --name <docint-account-name> \
  --resource-group rg-agon-dev-swc-data \
  --location swedencentral
```

Both infra workflows now include a preflight warning step to detect this condition before `what-if`/deploy.

## Security Posture (Dev Baseline)

- Internet ingress through Application Gateway only
- Public ingress should be HTTPS-first (HTTP kept only for redirect during migration)
- App Service public network access disabled
- App Service reachable privately via Private Endpoint from Application Gateway subnet
- Key Vault/Redis/PostgreSQL on private networking paths
- Runtime secrets via Key Vault references + managed identity
