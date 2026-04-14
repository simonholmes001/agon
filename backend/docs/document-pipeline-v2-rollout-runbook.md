# Document Pipeline v2 Rollout Runbook (Epic #472)

This runbook covers migration, rollout, diagnostics, and rollback for attachment extraction + large-document chunk-loop analysis.

## Scope

- Canonical `document.parse` flow for attachment ingestion
- Lifecycle states: `uploaded`, `extracting`, `ready`, `failed`
- Chunk-loop analysis for large extracted documents (no RAG/vector index)

## Migration and Backfill

Schema migration:
- `20260413090549_AddAttachmentExtractionLifecycleState`

Data backfill migration:
- `20260413143000_BackfillAttachmentExtractionLifecycleState`

Backfill behavior:
- Legacy rows with non-empty `extracted_text` are set to `extraction_status='ready'`.
- Legacy rows without extracted text are set to `extraction_status='failed'` with deterministic reason:
  - `Text extraction was not available for this legacy attachment. Re-upload the file to analyze it.`

## Pre-Deployment Checklist

1. Confirm app settings are explicitly set for this environment:
   - `AttachmentProcessing__MaxExtractedTextChars`
   - `AttachmentProcessing__Validation__*`
   - `AttachmentProcessing__TransientRetry__*`
   - `AttachmentProcessing__ChunkLoop__*`
2. Confirm one extraction backend is enabled:
   - `AttachmentProcessing__DocumentIntelligence__Enabled=true` and/or
   - `AttachmentProcessing__OpenAiVision__Enabled=true`
3. Confirm storage and metadata persistence are healthy:
   - blob storage reachable
   - PostgreSQL reachable

## Rollout Phases

1. Phase 0: Deploy binaries + migrations with chunk-loop enabled but conservative limits.
2. Phase 1: Validate upload/extraction on text, image, PDF, Office files.
3. Phase 2: Validate long-document behavior (`ready` extraction + chunk-loop synthesis).
4. Phase 3: Raise limits gradually if latency/error budgets remain healthy.

## Regression Corpus

- Integration test corpus fixture:
  - `backend/tests/Agon.Integration.Tests/Fixtures/large-document-corpus.json`
- Regression gate test:
  - `LargeDocumentRegressionCorpusIntegrationTests`

## Go/No-Go Criteria

Go if all are true:
- `agon.document_parse.failure` stays within normal baseline.
- `agon.attachment_chunk_loop.activations` and `agon.attachment_chunk_loop.passes` are non-zero for large documents.
- No spike in `ATTACHMENT_SIZE_LIMIT_EXCEEDED` and `ATTACHMENT_UNSUPPORTED_FORMAT` beyond expected traffic mix.
- No recurring secure-URL refusal drift in model responses for attachments with extracted text.

No-Go if any are true:
- sustained transient backend failures (`429/5xx`) without recovery from retry policy
- elevated extraction timeout failures
- unacceptable prelude latency from chunk-loop under production load

## Verification Queries (PostgreSQL)

```sql
-- Lifecycle distribution
SELECT extraction_status, COUNT(*) AS total
FROM session_attachments
GROUP BY extraction_status
ORDER BY extraction_status;

-- Legacy backfill reason count
SELECT COUNT(*) AS legacy_backfill_failed
FROM session_attachments
WHERE extraction_status = 'failed'
  AND extraction_failure_reason = 'Text extraction was not available for this legacy attachment. Re-upload the file to analyze it.';

-- Inconsistent rows that require operator review
SELECT id, session_id, file_name, extraction_status, LENGTH(COALESCE(extracted_text, '')) AS extracted_chars
FROM session_attachments
WHERE (extraction_status = 'ready' AND COALESCE(BTRIM(extracted_text), '') = '')
   OR (extraction_status IN ('uploaded', 'extracting') AND COALESCE(BTRIM(extracted_text), '') <> '')
LIMIT 200;
```

## Kill-Switches and Rollback Levers

Fast mitigation settings:
- Disable chunk-loop:
  - `AttachmentProcessing__ChunkLoop__Enabled=false`
- Reduce extraction pressure:
  - lower `AttachmentProcessing__Validation__Max*UploadBytes`
  - lower `AttachmentProcessing__MaxExtractedTextChars`
- Reduce retry amplification:
  - lower `AttachmentProcessing__TransientRetry__MaxAttempts`

Extraction backend rollback options:
- Temporarily disable Document Intelligence:
  - `AttachmentProcessing__DocumentIntelligence__Enabled=false`
- Temporarily disable OpenAI Vision:
  - `AttachmentProcessing__OpenAiVision__Enabled=false`

## Operator Diagnostics

Use these telemetry signals together:
- Parser:
  - `agon.document_parse.success`
  - `agon.document_parse.failure` (tags: `error_code`, `retryable`)
  - `agon.document_parse.duration.ms`
- Chunk-loop:
  - `agon.attachment_chunk_loop.activations`
  - `agon.attachment_chunk_loop.attachments`
  - `agon.attachment_chunk_loop.passes`
  - `agon.attachment_chunk_loop.responses`
  - `agon.attachment_chunk_loop.notes_generated`
  - `agon.attachment_chunk_loop.prelude.duration.ms`
- Azure alert rules (when `documentPipelineAlertsEnabled=true` in IaC):
  - `alert-<prefix>-doc-parse-failures`
  - `alert-<prefix>-chunk-loop-latency`

## Incident Playbook (Minimal)

1. Identify failure mode (`unsupported`, `oversize`, `timeout`, `transient_backend_failure`, `no_extractable_text`, `unexpected_failure`).
2. Verify backend connectivity and throttling posture.
3. Apply the smallest configuration rollback lever.
4. Retry a known test corpus document.
5. If unresolved, keep chunk-loop disabled and restore after root cause is resolved.
