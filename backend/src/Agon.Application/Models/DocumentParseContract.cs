namespace Agon.Application.Models;

public enum DocumentParseRoute
{
    Unsupported = 0,
    Text = 1,
    Image = 2,
    Document = 3
}

public enum DocumentParseErrorCode
{
    UnsupportedFormat = 0,
    Oversize = 1,
    Timeout = 2,
    NoExtractableText = 3,
    TransientBackendFailure = 4,
    UnexpectedFailure = 5
}

public sealed record DocumentParseRequest(
    byte[] Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? MaxAllowedBytes = null);

public sealed record DocumentParseResult(
    string ContractVersion,
    DocumentParseRoute Route,
    bool Success,
    bool Retryable,
    bool IsPartial,
    string? ExtractedText,
    int ExtractedTextChars,
    DocumentParseErrorCode? ErrorCode,
    string? FailureReason);
