# PR: Dev Deployment Pipeline + CLI Shell Input UX + README Additions

## Summary

This PR bundles three related improvements:

1. **Dev backend CD pipeline**: build/test backend, push Docker image to Docker Hub, deploy to Azure App Service, and verify health via Application Gateway.
2. **CLI shell UX improvements**: Codex-style wrapped prompt input plus cursor navigation/editing inside the prompt box.
3. **README additions**: etymology of "Agon" and a practical local deployment runbook (backend + required Postgres/Redis containers + local clients).

---

## Scope of Changes

### 1) Backend CD (Dev)

Added workflow:

- `.github/workflows/backend-deploy-dev.yaml`

Behavior:

1. Trigger on push to `main` for backend/workflow changes (or manual dispatch).
2. Run backend restore/build/test.
3. Validate Docker Hub secrets.
4. Build and push backend image to Docker Hub (`sha-*` + `dev-latest` tags).
5. Login to Azure via OIDC.
6. Resolve App Service name by configured prefix.
7. Point App Service to the pushed Docker image and set `WEBSITES_PORT=8080`.
8. Restart App Service.
9. Health-check `/health` through Application Gateway public IP.

Related files:

- `backend/Dockerfile` (container image build definition)
- `backend/.dockerignore` (build context optimization)

---

### 2) Infrastructure Deploy Workflow Hardening (Dev)

Updated:

- `.github/workflows/infrastructure-deploy-dev.yaml`

Key updates:

1. Node 24-forward compatibility flag in workflow env.
2. Explicit secret validation for `AZURE_POSTGRES_ADMIN_PASSWORD`.
3. Bicep compile validation expanded to all relevant modules.
4. Dev deployment step parameterization for optional LLM keys.

---

### 3) Infrastructure Setup Documentation

Updated:

- `infrastructure/DEV-SETUP.md`

What was added/clarified:

1. Current target architecture (App Gateway + App Service + private data plane).
2. Resource group strategy (`net` / `app` / `data` split).
3. Exact OIDC setup steps (App Registration, federated credentials, RBAC).
4. Required GitHub secrets for infra + backend container deploy.
5. Common deployment errors and fixes.
6. Legacy Front Door cleanup commands.

---

### 4) CLI Shell Input Experience

Updated:

- `cli/src/shell/renderer.ts`
- `cli/src/commands/shell.ts`
- `cli/test/shell/renderer.test.ts`

Implemented:

1. **Wrapped prompt input** in the shell input zone (multi-line within frame).
2. **Cursor movement/editing support**:
   - Left/Right arrows
   - Up/Down navigation across wrapped lines
   - Option/Meta word jumps (`Option+Left/Right`, plus `Meta+b/f` fallback)
   - Insertion at cursor (not append-only)
   - Backspace/Delete at cursor
   - `Ctrl+a` / `Ctrl+e` (line start/end)
   - `Ctrl+w` (delete previous word)

---

### 5) README Additions (No Existing Content Removed)

Updated:

- `README.md`

Added:

1. Short explanation of **"Agon"** Greek root (`ἀγών`) and naming rationale.
2. **Local Deployment (Developer Runbook)** section including:
   - Start local Postgres + Redis containers:
     - `cd backend && docker compose up -d postgres redis`
   - Optional pgAdmin profile
   - Run backend API:
     - `dotnet run --project src/Agon.Api/Agon.Api.csproj`
   - Run local CLI and frontend
   - Stop local containers:
     - `docker compose down`

---

## Validation

Validated in local commit hooks during this PR branch work:

1. CLI test suite passing (Vitest).
2. Backend test suite passing (`dotnet test` on solution).
3. CLI build passing (`tsc`) for shell input changes.

---

## Operational Requirements

For backend CD workflow to succeed in GitHub Actions:

1. Azure OIDC secrets:
   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`
2. Infra secret:
   - `AZURE_POSTGRES_ADMIN_PASSWORD`
3. Docker Hub secrets:
   - `DOCKERHUB_USERNAME`
   - `DOCKERHUB_TOKEN`
   - `DOCKERHUB_IMAGE_NAME`
4. Optional LLM provider secrets:
   - `OPENAI_KEY`, `CLAUDE_KEY`, `GEMINI_KEY`, `DEEPSEEK_KEY`

---

## Impact

1. Establishes a concrete dev backend deployment path from `main` to Azure runtime.
2. Improves shell usability significantly for long and edited prompts.
3. Makes local developer setup clearer and faster.

