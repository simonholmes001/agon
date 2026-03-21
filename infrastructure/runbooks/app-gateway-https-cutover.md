# App Gateway HTTPS Cutover Runbook (Dev)

Use this runbook to move Agon dev ingress from HTTP-by-IP to HTTPS-by-hostname.

## Scope

- Environment: `dev`
- Resource group pattern: `rg-<namePrefix>-app`
- Components: Application Gateway listener/rules, DNS record, CLI hosted endpoint

## Preconditions

1. Public API hostname exists (for example: `api-dev.example.com`).
2. DNS can be updated to the Application Gateway public IP.
3. Listener certificate (PFX + password) is available and trusted by clients.
4. GitHub environment secrets are configured:
   - `APP_GATEWAY_SSL_CERTIFICATE_PFX_BASE64`
   - `APP_GATEWAY_SSL_CERTIFICATE_PASSWORD`
   - `AGON_API_HOSTNAME`
   - optional `APP_GATEWAY_SSL_POLICY_NAME`

## Phase 1: Prechecks

1. Verify current gateway health over HTTP (legacy path):
```bash
curl -i "http://<gateway-ip>/health"
```

2. Verify target DNS resolves to the expected gateway IP:
```bash
dig +short api-dev.example.com
```

3. Verify certificate matches hostname:
```bash
echo | openssl s_client -connect api-dev.example.com:443 -servername api-dev.example.com 2>/dev/null | openssl x509 -noout -subject -issuer -dates
```

## Phase 2: Deploy HTTPS Listener

1. Merge/deploy infra changes to `main` so `infrastructure-deploy-dev.yaml` runs with HTTPS parameters.
2. Confirm deployment completed successfully.

## Phase 3: Validate

1. Validate HTTPS health:
```bash
curl -i "https://api-dev.example.com/health"
```

2. Validate HTTP redirect behavior:
```bash
curl -I "http://api-dev.example.com/health"
```

Expected: redirect (`301` or `302`) to `https://...`.

3. Validate CLI:
```bash
agon config set apiUrl "https://api-dev.example.com"
agon sessions
agon status
```

## Phase 4: Stabilization

1. Monitor backend health and error rates.
2. Watch App Gateway access/error logs and backend probe health.
3. Track CLI command success/error rates.

## Rollback

If HTTPS health fails after deployment:

1. Temporarily point CLI back to legacy endpoint:
```bash
agon config set apiUrl "http://<gateway-ip>"
```

2. Remove HTTPS secret inputs from deploy environment (or set empty values), redeploy infra, and confirm HTTP health path is restored.
3. Investigate certificate/DNS mismatch before retrying cutover.

## Success Criteria

- HTTPS endpoint is healthy and stable.
- HTTP path redirects to HTTPS.
- CLI works with HTTPS hostname without disabling TLS verification.
