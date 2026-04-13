namespace Agon.Api.Configuration;

/// <summary>
/// Configuration section for attachment extraction behavior.
/// </summary>
public sealed class AttachmentProcessingConfiguration
{
    public const string SectionName = "AttachmentProcessing";

    public int MaxExtractedTextChars { get; set; } = 200000;
    public DocumentIntelligenceProcessingConfiguration DocumentIntelligence { get; set; } = new();
    public OpenAiVisionProcessingConfiguration OpenAiVision { get; set; } = new();
    public AttachmentChunkLoopConfiguration ChunkLoop { get; set; } = new();
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
    public int ChunkSizeChars { get; set; } = 12000;
    public int ChunkOverlapChars { get; set; } = 1000;
    public int MaxChunksPerAttachment { get; set; } = 20;
    public int MaxChunkNoteChars { get; set; } = 1200;
    public int MaxFinalNotesPerAgent { get; set; } = 8;
}
