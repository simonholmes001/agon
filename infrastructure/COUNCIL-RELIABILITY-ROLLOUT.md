# Council Reliability Rollout (Epic #558)

## Target State

- Council runs emit explicit stage progress: `analysis`, `chunking`, `critique`, `synthesis`, `completed`, `failed`.
- CLI council watch remains active until terminal completion/failure or explicit user interrupt.
- Agent-level failures are isolated; one agent failure does not terminate the whole run.
- Large-document chunk prelude work is capped to control latency and token cost.

## SLOs

- `CouncilFirstProgressP95 <= 90s`
- `CouncilCompletionSuccessRate >= 98%` (excluding explicit user interrupts)
- `CouncilSilentFailureRate = 0%` (every failure must publish `council_failed`)
- `CouncilMedianDurationLargeDoc <= baseline * 0.75` after optimization rollout

## Required Metrics

- `council_invoked_total`
- `council_progress_stage_total{stage=...}`
- `council_completed_total`
- `council_failed_total{reason_code=...}`
- `council_stage_duration_ms{stage=...}`
- `council_token_usage_total{provider,model}`
- `council_chunk_prelude_pass_total`

## Azure Guardrails

- Use Application Insights + Log Analytics for run telemetry and stage timelines.
- Add Azure Monitor alerts for:
  - `council_failed_total` spike
  - missing `council_progress` for active runs
  - elevated duration and token anomalies
- Route alerts to on-call channel with run/session identifiers.

## Rollout Plan

1. Deploy with feature flag enabled in `dev` only.
2. Validate SLO telemetry and failure reason-code quality for 48h.
3. Enable in `stage` with canary cohort.
4. Enable in `prod` gradually (10% -> 50% -> 100%).

## Rollback

- Disable council reliability feature flag.
- Revert chunk-loop cap changes if token/quality regression is observed.
- Keep `council_failed` terminal messaging enabled for operator visibility.
