namespace Agon.Infrastructure.Attachments;

/// <summary>
/// Runtime options for uploaded attachment text extraction.
/// </summary>
public sealed class AttachmentExtractionOptions
{
    public int MaxExtractedTextChars { get; set; } = 12000;
    public int MaxExtractionFileBytes { get; set; } = 10 * 1024 * 1024;
    public DocumentIntelligenceExtractionOptions DocumentIntelligence { get; set; } = new();
    public OpenAiVisionExtractionOptions OpenAiVision { get; set; } = new();
}

public sealed class DocumentIntelligenceExtractionOptions
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

public sealed class OpenAiVisionExtractionOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string FallbackModel { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 1200;
    public string Detail { get; set; } = "auto";
    public int MaxImageBytes { get; set; } = 20 * 1024 * 1024;
}
