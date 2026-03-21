# ADR: HTTPS Edge Endpoint Strategy (Dev)

Date: 2026-03-21
Status: Accepted (Dev)

## Context

Agon CLI currently supports a hosted backend endpoint and local overrides. The Azure App Service backend is private and already reached by Application Gateway over HTTPS, but client ingress has historically used HTTP by public IP. We need a stable HTTPS-first edge contract aligned with Azure networking and security best practices.

## Decision

1. Public ingress is HTTPS-first using Application Gateway listener on 443.
2. HTTP 80 remains only to redirect to HTTPS during migration.
3. The canonical client endpoint is a DNS hostname (`AGON_API_HOSTNAME`), not the raw gateway IP.
4. Certificate material is provided to deployment via environment secrets (`APP_GATEWAY_SSL_CERTIFICATE_PFX_BASE64` and `APP_GATEWAY_SSL_CERTIFICATE_PASSWORD`) and bound to the HTTPS listener.
5. Deployment pipeline validates:
   - HTTPS `/health` success over hostname.
   - HTTP-to-HTTPS redirect behavior.
6. CLI hosted endpoint precedence is:
   - `AGON_API_URL` (explicit override)
   - `AGON_HOSTED_API_URL` (full URL)
   - `AGON_API_HOSTNAME` (`https://<hostname>`)
   - legacy fallback

## Rationale

- DNS hostnames are required for normal TLS hostname verification.
- Hostname-based endpoint contracts are more stable than IP-based contracts during gateway replacement.
- Redirect validation in CI prevents silent regressions where HTTP stays active without secure upgrade.
- Keeping local override precedence preserves developer workflows.

## Alternatives Considered

1. Keep HTTP by IP as default: rejected (transport security and trust risk).
2. Use HTTPS by IP directly: rejected for standard PKI hostname validation mismatch.
3. Hard cut to HTTPS-only without redirect: deferred to a later hardening phase to reduce migration risk.

## Operational Requirements

- Configure GitHub environment secrets:
  - `APP_GATEWAY_SSL_CERTIFICATE_PFX_BASE64`
  - `APP_GATEWAY_SSL_CERTIFICATE_PASSWORD`
  - `AGON_API_HOSTNAME`
  - optional `APP_GATEWAY_SSL_POLICY_NAME`
- Ensure DNS record for `AGON_API_HOSTNAME` points to current gateway public IP.
- Ensure certificate SAN/CN matches `AGON_API_HOSTNAME`.

## Consequences

- Positive: secure default ingress, stable client endpoint, safer cutovers.
- Tradeoff: operational overhead for cert and DNS lifecycle.
- Residual risk: if cert rotation is not automated, expiration can impact availability; monitor expiry and rehearse replacement.

## Follow-up

- Move certificate storage/rotation to Key Vault integration for reduced secret handling risk.
- Consider enforcing HTTPS-only listener (remove port 80 redirect) after migration stabilization.
