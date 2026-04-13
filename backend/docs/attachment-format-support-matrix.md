# Attachment Format Support Matrix

This matrix defines deterministic attachment routing for extraction in Agon backend.

## Routing precedence

1. Use known `Content-Type` first (`text/*`, known text, known image, known document).
2. If `Content-Type` is unknown or generic binary (`application/octet-stream`), fall back to file extension.
3. If neither matches, route is `Unsupported` and extraction returns `null`.

## Supported routes

| Route | Content types | Extensions | Extraction path |
|---|---|---|---|
| Text | `text/*`, `application/json`, `application/xml`, `application/x-yaml`, `application/yaml`, `text/csv`, `application/csv`, `application/x-www-form-urlencoded`, `application/javascript`, `application/x-javascript`, `application/typescript`, `application/sql`, `application/rtf` | `.txt`, `.md`, `.markdown`, `.json`, `.yaml`, `.yml`, `.csv`, `.xml`, `.html`, `.htm`, `.log`, `.ini`, `.cfg`, `.conf`, `.toml`, `.sql`, `.ts`, `.js`, `.tsx`, `.jsx`, `.cs`, `.py`, `.java`, `.go`, `.rb`, `.php`, `.ps1`, `.sh`, `.bat`, `.env`, `.rtf` | UTF-8 text extraction + normalization |
| Image | `image/png`, `image/jpeg`, `image/jpg`, `image/pjpeg`, `image/gif`, `image/bmp`, `image/webp`, `image/tiff`, `image/heic`, `image/heif` | `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.webp`, `.tif`, `.tiff`, `.heic`, `.heif`, `.jfif` | OpenAI Vision first, then Document Intelligence OCR fallback |
| Document | `application/pdf`, `application/msword`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.ms-excel`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.ms-powerpoint`, `application/vnd.openxmlformats-officedocument.presentationml.presentation` | `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.ppt`, `.pptx` | Document Intelligence extraction |

## Notes

- Mismatched metadata is resolved deterministically by precedence. Example: `text/plain` with `.pdf` routes as `Text`.
- Generic binary uploads rely on extension fallback. Example: `application/octet-stream` with `.pptx` routes as `Document`.
