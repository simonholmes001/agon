# `document.parse` Contract (v1.0)

This document defines the canonical parser contract used by Agon attachment workflows.

## Objective

- Provide a single, deterministic parse contract for supported document and image formats.
- Normalize failure taxonomy so callers can handle parse outcomes predictably.
- Preserve compatibility as the parser evolves by versioning the contract.

## Request Model

`DocumentParseRequest`

- `content`: raw file bytes.
- `fileName`: attachment name used for extension-based routing.
- `contentType`: MIME type hint (normalized before routing).
- `sizeBytes`: declared payload size.
- `maxAllowedBytes` (optional): caller-enforced size limit for oversize checks.

## Response Model

`DocumentParseResult`

- `contractVersion`: current contract version (`1.0`).
- `route`: `text | image | document | unsupported`.
- `success`: parse success flag.
- `retryable`: indicates whether caller should retry later.
- `isPartial`: partial-success marker (reserved for future structured extraction paths).
- `extractedText`: full normalized extracted text when `success=true`.
- `extractedTextChars`: extracted text character count.
- `errorCode`: deterministic failure class when `success=false`.
- `failureReason`: stable user/operator-facing failure reason.

## Error Taxonomy

- `unsupported_format`: MIME/extension is not in supported routing matrix.
- `oversize`: payload exceeds caller max size contract.
- `timeout`: extraction timed out before completion.
- `no_extractable_text`: parser completed but found no usable text.
- `transient_backend_failure`: temporary dependency failure (retryable).
- `unexpected_failure`: non-retryable unclassified parser failure.

## Caller Behavior

- `success=true`: persist extracted text and mark attachment `ready`.
- `success=false && retryable=true`: mark attachment `failed` with retry-capable reason (operator can replay).
- `success=false && retryable=false`: mark attachment `failed` with deterministic reason and require user/operator action.

## Telemetry

Canonical parser operations emit:

- `agon.document_parse.success` (counter)
- `agon.document_parse.failure` (counter with `error_code` and `retryable` tags)
- `agon.document_parse.duration.ms` (histogram)
