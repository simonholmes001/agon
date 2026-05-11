# ADR: Replace Application Gateway API Ingress with Azure API Management

## Status
Proposed

## Context
Agon currently deploys Azure Application Gateway as the primary public API ingress. Cost optimization efforts on gateway scheduling and SKU tuning have not reduced spend sufficiently.

The platform needs a lower-cost ingress model with stronger API policy controls while preserving:
- API reliability and observability
- clear rollback path
- compatibility with existing backend API behavior

This decision applies to API ingress for Agon backend endpoints and does not redefine broader platform perimeter controls.

## Decision
Adopt Azure API Management (APIM) as the primary API ingress for Agon in dev, with Application Gateway retained as a controlled fallback path.

In this phase:
- `deployApiManagement=true` and `deployApplicationGateway=false` by default.
- APIM publishes `Agon API` under `/api` and forwards to the existing App Service backend.
- App Gateway code paths remain in IaC for reversible rollback.

## Alternatives Considered
1. Keep Application Gateway only
- Lowest migration risk
- Does not address cost goal sufficiently

2. APIM-only replacement (selected for this phase)
- Strong API policy and productization surface
- Potentially lower edge costs in low-to-moderate traffic profiles
- Requires careful security posture review due to backend exposure changes

3. Front Door + APIM
- Strong edge/WAF posture for internet APIs
- Rejected for this phase due to added cost and complexity

4. App Gateway + APIM hybrid long-term
- Minimizes cutover risk
- Reduces expected cost benefits

## Consequences
### Positive
- API ingress policy centralization in APIM
- Explicit API boundary that aligns with adapter-style edge concerns
- Cleaner API lifecycle controls (versioning, policy, products)

### Negative / Tradeoffs
- APIM does not provide WAF parity by itself
- Current migration enables backend public network access when APIM is enabled; this is a temporary exposure tradeoff
- Additional APIM policy/test ownership required

### Risks and Mitigations
- Risk: edge security regression
  - Mitigation: enforce strict APIM policies, backend auth, request throttling, and continuous monitoring
- Risk: routing/auth regressions during cutover
  - Mitigation: parallel fallback path via retained App Gateway IaC and output-based endpoint switch
- Risk: APIM SKU cost mismatch vs traffic profile
  - Mitigation: validate with real traffic and monthly cost telemetry before production decision

## Implementation Plan
1. Foundation
- Introduce APIM deployment controls and outputs in IaC
- Keep App Gateway deployment path available for rollback

2. Migration
- Route API traffic to APIM endpoint in non-production
- Validate health (`/health`), auth, and end-to-end API flows

3. Hardening
- Add APIM policy set (rate limiting, JWT validation where appropriate, request/response controls)
- Tighten APIM policy baseline and backend access controls

4. Production rollout
- Controlled cutover with explicit rollback switch (`deployApplicationGateway=true` fallback)

## Validation
Success criteria:
- APIM endpoint serves API traffic successfully in dev
- Existing backend health and auth flows remain functional
- Monitoring/logging visibility preserved in Log Analytics/App Insights
- Cost profile meets target over at least one billing cycle

## Review Trigger
Reassess this decision when one of the following occurs:
- production rollout planning begins
- APIM monthly cost exceeds Application Gateway baseline for sustained periods
- security review requires WAF-grade control at edge
