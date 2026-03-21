# App Gateway HTTPS Observability Runbook (Dev)

Use this runbook to verify edge TLS and redirect behavior after deployment.

## Scope

- Environment: `dev`
- Data source: Log Analytics workspace attached to Application Gateway diagnostics

## Prerequisite

Application Gateway diagnostics must be enabled to Log Analytics (configured in IaC).

## Quick Checks

1. HTTPS health:
```bash
curl -i "https://<api-host>/health"
```

2. HTTP redirect:
```bash
curl -I "http://<api-host>/health"
```

Expected: `30x` redirect with `Location: https://<api-host>/...`.

## Log Analytics Queries (KQL)

1. Redirect behavior trend (30x responses):
```kusto
AzureDiagnostics
| where ResourceProvider == "MICROSOFT.NETWORK"
| where Category == "ApplicationGatewayAccessLog"
| where TimeGenerated > ago(24h)
| summarize redirectCount = countif(toint(httpStatus_d) between (300 .. 399)),
            nonRedirectHttpCount = countif(toint(httpStatus_d) !between (300 .. 399) and requestUri_s has "/health")
```

2. HTTPS success/failure rate:
```kusto
AzureDiagnostics
| where ResourceProvider == "MICROSOFT.NETWORK"
| where Category == "ApplicationGatewayAccessLog"
| where TimeGenerated > ago(24h)
| summarize success = countif(toint(httpStatus_d) between (200 .. 399)),
            failures = countif(toint(httpStatus_d) >= 400)
```

3. Backend probe health failures:
```kusto
AzureDiagnostics
| where ResourceProvider == "MICROSOFT.NETWORK"
| where Category == "ApplicationGatewayPerformanceLog"
| where TimeGenerated > ago(24h)
| where message has "Unhealthy" or message has "Probe"
| project TimeGenerated, message
| order by TimeGenerated desc
```

## Release Gate Recommendation

Treat deployment as unhealthy and block release if any of the following are true over a 30-minute post-deploy window:

1. HTTPS `/health` check fails.
2. HTTP `/health` does not redirect to HTTPS.
3. Backend probe unhealthy events persist for more than 5 consecutive minutes.
