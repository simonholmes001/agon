# Dev Infrastructure Setup (Azure + Bicep + GitHub OIDC)

This document describes the infrastructure delivery model for the `dev` environment.

## Target Architecture (Dev)

- Backend API: Azure App Service (Linux)
- Durable state: Azure Database for PostgreSQL - Flexible Server (private network)
- Cache/session state: Azure Cache for Redis (private endpoint)
- Secrets: Azure Key Vault (private endpoint)
- Telemetry: Application Insights + Log Analytics
- Network: VNet with separate subnets for App Service integration, private endpoints, and PostgreSQL delegation
- IaC: Bicep only
- CI/CD: GitHub Actions with OpenID Connect (no Azure client secret)

## Naming Convention

Prefix used in parameters: `agon-dev-frc`

Resource naming follows Azure CAF abbreviations and resource naming guidance:

- https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-naming?toc=%2Fazure%2Fazure-resource-manager%2Fmanagement%2Ftoc.json#example-azure-resource-names
- https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/azure-best-practices/resource-abbreviations
- https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules

## Environment Values (Current)

- Subscription ID: `c08d1de9-6131-427d-b974-e0b52c22eae1`
- Tenant ID: `17ca2540-dd3e-4204-b2f7-a3e3ad209719`
- Region: `francecentral`
- Prefix: `agon-dev-frc`
- Alert Email: `simonholmesabc@gmail.com`

## One-Time Azure Identity Setup (OIDC)

This section is required before GitHub Actions can deploy to Azure.

### 1) Create the App Registration

1. Azure Portal -> `Microsoft Entra ID` -> `App registrations` -> `New registration`
2. Name recommendation: `agon-gha-dev-deployer`
3. Supported account type: Single tenant
4. Register

Capture these values from the Overview page:

- `Application (client) ID` -> use as `AZURE_CLIENT_ID`
- `Directory (tenant) ID` -> use as `AZURE_TENANT_ID`

### 2) Confirm Service Principal Exists

1. Azure Portal -> `Microsoft Entra ID` -> `Enterprise applications`
2. Search app name (`agon-gha-dev-deployer`) or application ID
3. Open it and confirm:
   - Application ID matches your app registration
   - Object ID is present (used for RBAC troubleshooting)

### 3) Add Federated Credentials (GitHub OIDC)

On the same App Registration:

1. `Certificates & secrets` -> `Federated credentials` -> `Add credential`
2. Scenario: `GitHub Actions deploying Azure resources`
3. Add two credentials:

- Name: `github-main-deploy`
  - Organization: `simonholmes001`
  - Repository: `agon`
  - Entity type: `Branch`
  - Branch: `main`
  - Subject becomes: `repo:simonholmes001/agon:ref:refs/heads/main`

- Name: `github-pr-validate`
  - Organization: `simonholmes001`
  - Repository: `agon`
  - Entity type: `Pull request`
  - Subject becomes: `repo:simonholmes001/agon:pull_request`

Audience must be:

- `api://AzureADTokenExchange`

### 4) Assign RBAC on Subscription

The workflows use subscription-scope deployments (`az deployment sub ...`), so role assignment must be on the subscription scope.

1. Azure Portal -> `Subscriptions` -> your subscription -> `Access control (IAM)` -> `Add role assignment`
2. Assign to the service principal (`agon-gha-dev-deployer`)
3. For current template, choose one of:

- `Owner` (simplest for dev), or
- `Contributor` + `User Access Administrator`

`Contributor` alone may fail because this template creates a Key Vault RBAC role assignment.

### 5) Wait for Propagation

After creating federated credentials and RBAC assignments, wait 5-10 minutes before re-running workflows.

## GitHub Secrets Required

Set these repository secrets before running deploy workflows:

- `AZURE_CLIENT_ID`: `7dd5200a-57b0-4d9a-9e30-302b339355f1`
- `AZURE_TENANT_ID`: `17ca2540-dd3e-4204-b2f7-a3e3ad209719`
- `AZURE_SUBSCRIPTION_ID`: `c08d1de9-6131-427d-b974-e0b52c22eae1`
- `AZURE_POSTGRES_ADMIN_PASSWORD`: strong bootstrap password for PostgreSQL admin

Optional secrets for LLM provider keys (stored in Key Vault at deploy time):

- `OPENAI_KEY`
- `CLAUDE_KEY`
- `GEMINI_KEY`
- `DEEPSEEK_KEY`

## Where to Find IDs in Azure Portal

- Client ID: `Entra ID -> App registrations -> <app> -> Overview -> Application (client) ID`
- Tenant ID: `Entra ID -> App registrations -> <app> -> Overview -> Directory (tenant) ID`
- Service Principal Object ID: `Entra ID -> Enterprise applications -> <app> -> Overview -> Object ID`

## Workflows

- PR validation (plan/validate only): `.github/workflows/infrastructure-validate.yaml`
  - Builds Bicep templates
  - Runs subscription-scope `what-if` against `dev`
- Main deployment: `.github/workflows/infrastructure-deploy-dev.yaml`
  - Triggered on `main` changes under `infrastructure/**` (and manual dispatch)
  - Runs subscription-scope deployment for `dev`

## Execution Flow

1. Open PR with infra changes -> `infrastructure-validate.yaml` runs:
   - Bicep build
   - Azure `what-if`
2. Merge to `main` -> `infrastructure-deploy-dev.yaml` runs:
   - Subscription-scope deployment
   - Creates/updates `dev` resource group and resources

## Deployment Scope

Templates deploy from subscription scope and create/update:

- Resource group: `rg-agon-dev-frc-app`
- All `dev` platform resources inside that resource group

## Common Errors and Fixes

### Error: `AADSTS700213: No matching federated identity record`

Cause: Missing or mismatched federated credential subject.

Fix:

- Ensure `github-pr-validate` exists with subject `repo:simonholmes001/agon:pull_request`
- Ensure `github-main-deploy` exists with subject `repo:simonholmes001/agon:ref:refs/heads/main`

### Error: `No subscriptions found for <client-id>`

Cause: Service principal has no RBAC on the subscription, or wrong tenant/subscription/client secret values.

Fix:

1. Verify `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
2. Assign required role(s) at subscription scope to `agon-gha-dev-deployer`
3. Wait for propagation and rerun

### Cannot find `Contributor`/`Owner` in IAM picker

Cause: current signed-in user lacks permissions to assign those roles.

Fix:

- Ask a subscription `Owner` or `User Access Administrator` to assign roles to the service principal.

## Notes

- App Service stays publicly reachable over HTTPS for CLI access.
- PostgreSQL, Redis, and Key Vault are deployed for private network access.
- App Service uses system-assigned managed identity and Key Vault references for runtime secrets.
