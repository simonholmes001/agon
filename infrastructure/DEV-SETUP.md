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

## Workflows

- PR validation (plan/validate only): `.github/workflows/infrastructure-validate.yaml`
  - Builds Bicep templates
  - Runs subscription-scope `what-if` against `dev`
- Main deployment: `.github/workflows/infrastructure-deploy-dev.yaml`
  - Triggered on `main` changes under `infrastructure/**` (and manual dispatch)
  - Runs subscription-scope deployment for `dev`

## Deployment Scope

Templates deploy from subscription scope and create/update:

- Resource group: `rg-agon-dev-frc-app`
- All `dev` platform resources inside that resource group

## Notes

- App Service stays publicly reachable over HTTPS for CLI access.
- PostgreSQL, Redis, and Key Vault are deployed for private network access.
- App Service uses system-assigned managed identity and Key Vault references for runtime secrets.
