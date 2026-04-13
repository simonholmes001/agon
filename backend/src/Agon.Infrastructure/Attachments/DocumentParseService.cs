using Agon.Application.Interfaces;
using Agon.Application.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Agon.Infrastructure.Attachments;

public sealed class DocumentParseService : IDocumentParser
{
    private const string ContractVersion = "1.0";

    private readonly IAttachmentTextExtractor _attachmentTextExtractor;
    private readonly ILogger<DocumentParseService> _logger;

    public DocumentParseService(
        IAttachmentTextExtractor attachmentTextExtractor,
        ILogger<DocumentParseService> logger)
    {
        _attachmentTextExtractor = attachmentTextExtractor;
        _logger = logger;
    }

    public async Task<DocumentParseResult> ParseAsync(
        DocumentParseRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);

        var normalizedContentType = NormalizeContentType(request.ContentType);
        var route = ToDocumentParseRoute(
            AttachmentRoutingPolicy.Resolve(request.FileName, normalizedContentType));

        if (route == DocumentParseRoute.Unsupported)
        {
            return Complete(Failure(
                route,
                DocumentParseErrorCode.UnsupportedFormat,
                "Attachment format is not supported.",
                retryable: false));
        }

        if (request.MaxAllowedBytes.HasValue && request.SizeBytes > request.MaxAllowedBytes.Value)
        {
            return Complete(Failure(
                route,
                DocumentParseErrorCode.Oversize,
                $"Attachment exceeds max allowed size of {request.MaxAllowedBytes.Value} bytes.",
                retryable: false));
        }

        try
        {
            var extractedText = await _attachmentTextExtractor.ExtractAsync(
                request.Content,
                request.FileName,
                normalizedContentType,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return Complete(Failure(
                    route,
                    DocumentParseErrorCode.NoExtractableText,
                    "No extractable text was produced for this attachment.",
                    retryable: false));
            }

            var normalizedText = extractedText.Trim();
            return Complete(new DocumentParseResult(
                ContractVersion,
                route,
                Success: true,
                Retryable: false,
                IsPartial: false,
                ExtractedText: normalizedText,
                ExtractedTextChars: normalizedText.Length,
                ErrorCode: null,
                FailureReason: null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Document parse timed out for file {FileName}.",
                request.FileName);
            return Complete(Failure(
                route,
                DocumentParseErrorCode.Timeout,
                "Attachment extraction timed out.",
                retryable: true));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Transient backend failure during document parse for file {FileName}.",
                request.FileName);
            return Complete(Failure(
                route,
                DocumentParseErrorCode.TransientBackendFailure,
                "Attachment extraction backend is temporarily unavailable.",
                retryable: true));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unexpected document parse failure for file {FileName}.",
                request.FileName);
            return Complete(Failure(
                route,
                DocumentParseErrorCode.UnexpectedFailure,
                "Attachment extraction failed.",
                retryable: false));
        }

        DocumentParseResult Complete(DocumentParseResult result)
        {
            var tags = new TagList
            {
                { "route", ToRouteTag(result.Route) }
            };
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (result.Success)
            {
                DocumentParseMetrics.ParseSuccess.Add(1, tags);
            }
            else
            {
                tags.Add("retryable", result.Retryable);
                tags.Add("error_code", ToErrorCodeTag(result.ErrorCode));
                DocumentParseMetrics.ParseFailure.Add(1, tags);
            }

            DocumentParseMetrics.ParseDurationMs.Record(elapsedMs, tags);
            return result;
        }
    }

    private static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        var semicolonIndex = contentType.IndexOf(';');
        return (semicolonIndex >= 0 ? contentType[..semicolonIndex] : contentType)
            .Trim()
            .ToLowerInvariant();
    }

    private static DocumentParseRoute ToDocumentParseRoute(AttachmentRoutingRoute route)
    {
        return route switch
        {
            AttachmentRoutingRoute.Text => DocumentParseRoute.Text,
            AttachmentRoutingRoute.Image => DocumentParseRoute.Image,
            AttachmentRoutingRoute.Document => DocumentParseRoute.Document,
            _ => DocumentParseRoute.Unsupported
        };
    }

    private static DocumentParseResult Failure(
        DocumentParseRoute route,
        DocumentParseErrorCode errorCode,
        string reason,
        bool retryable)
    {
        return new DocumentParseResult(
            ContractVersion,
            route,
            Success: false,
            Retryable: retryable,
            IsPartial: false,
            ExtractedText: null,
            ExtractedTextChars: 0,
            ErrorCode: errorCode,
            FailureReason: reason);
    }

    private static string ToRouteTag(DocumentParseRoute route)
    {
        return route.ToString().ToLowerInvariant();
    }

    private static string ToErrorCodeTag(DocumentParseErrorCode? errorCode)
    {
        if (errorCode is null)
        {
            return "none";
        }

        return errorCode.Value switch
        {
            DocumentParseErrorCode.UnsupportedFormat => "unsupported_format",
            DocumentParseErrorCode.Oversize => "oversize",
            DocumentParseErrorCode.Timeout => "timeout",
            DocumentParseErrorCode.NoExtractableText => "no_extractable_text",
            DocumentParseErrorCode.TransientBackendFailure => "transient_backend_failure",
            DocumentParseErrorCode.UnexpectedFailure => "unexpected_failure",
            _ => "unknown"
        };
    }
}
