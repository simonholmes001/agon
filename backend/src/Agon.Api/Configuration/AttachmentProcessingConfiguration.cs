namespace Agon.Api.Configuration;

/// <summary>
/// Configuration section for attachment extraction behavior.
/// </summary>
public sealed class AttachmentProcessingConfiguration
{
    public const string SectionName = "AttachmentProcessing";

    public int MaxExtractedTextChars { get; set; } = 200000;
    public AttachmentValidationConfiguration Validation { get; set; } = new();
    public AttachmentAsyncExtractionConfiguration AsyncExtraction { get; set; } = new();
    public DocumentIntelligenceProcessingConfiguration DocumentIntelligence { get; set; } = new();
    public OpenAiVisionProcessingConfiguration OpenAiVision { get; set; } = new();
    public AttachmentTransientRetryConfiguration TransientRetry { get; set; } = new();
    public AttachmentChunkLoopConfiguration ChunkLoop { get; set; } = new();
}

public sealed class AttachmentValidationConfiguration
{
    public bool RejectUnsupportedFormats { get; set; } = true;
    public int MaxUploadBytes { get; set; } = 25 * 1024 * 1024;
    public int MaxTextUploadBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxDocumentUploadBytes { get; set; } = 25 * 1024 * 1024;
    public int MaxImageUploadBytes { get; set; } = 20 * 1024 * 1024;
}

public sealed class AttachmentAsyncExtractionConfiguration
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 20;
    public int PollIntervalMs { get; set; } = 1000;
    public bool RequeueStaleExtractingEnabled { get; set; } = true;
    public int StaleExtractingAfterMinutes { get; set; } = 15;
    public int ReconcileIntervalMs { get; set; } = 30000;
    public int QueueCapacity { get; set; } = 0; // Legacy alias; if > 0 and BatchSize is unset, it becomes BatchSize.
}

public sealed class DocumentIntelligenceProcessingConfiguration
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = string.Empty;
    public string ModelId { get; set; } = "prebuilt-layout";
    public string ApiVersion { get; set; } = "2024-11-30";
    public bool UseManagedIdentity { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public int PollIntervalMs { get; set; } = 1500;
    public int MaxPollAttempts { get; set; } = 40;
}

public sealed class OpenAiVisionProcessingConfiguration
{
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "gpt-4o-mini";
    public string FallbackModel { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 1200;
    public string Detail { get; set; } = "auto";
    public int MaxImageBytes { get; set; } = 20971520;
}

public sealed class AttachmentChunkLoopConfiguration
{
    public bool Enabled { get; set; } = true;
    public int ActivationThresholdChars { get; set; } = 14000;
    public int ChunkSizeChars { get; set; } = 9000;
    public int ChunkOverlapChars { get; set; } = 1000;
    public bool UseTokenAwareSizing { get; set; } = true;
    public int TargetChunkTokens { get; set; } = 2200;
    public int EstimatedCharsPerToken { get; set; } = 4;
    public bool EnableQueryFocusedSecondPass { get; set; } = false;
    public int MaxFocusedChunksPerAttachment { get; set; } = 2;
    public int MinQueryKeywordLength { get; set; } = 4;
    public int MaxChunksPerAttachment { get; set; } = 12;
    public int MaxChunkNoteChars { get; set; } = 900;
    public int MaxFinalNotesPerAgent { get; set; } = 6;
    public int MaxPreludePasses { get; set; } = 8;
    public int MaxChunkBudgetChars { get; set; } = 180000;
    public int EarlyExitMinNotesPerAgent { get; set; } = 3;
}

public sealed class AttachmentTransientRetryConfiguration
{
    public int MaxAttempts { get; set; } = 3;
    public int BaseDelayMs { get; set; } = 250;
    public int MaxDelayMs { get; set; } = 2000;
}

public sealed class AttachmentUploadValidationOptions
{
    public bool RejectUnsupportedFormats { get; init; } = true;
    public int MaxUploadBytes { get; init; } = 25 * 1024 * 1024;
    public int MaxTextUploadBytes { get; init; } = 10 * 1024 * 1024;
    public int MaxDocumentUploadBytes { get; init; } = 25 * 1024 * 1024;
    public int MaxImageUploadBytes { get; init; } = 20 * 1024 * 1024;
}
