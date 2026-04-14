namespace Agon.Application.Orchestration;

/// <summary>
/// Controls context-window chunk-loop behavior for long attachment analysis.
/// </summary>
public sealed class AttachmentChunkLoopOptions
{
    public bool Enabled { get; set; } = true;
    public int ActivationThresholdChars { get; set; } = 14000;
    public int ChunkSizeChars { get; set; } = 12000;
    public int ChunkOverlapChars { get; set; } = 1000;
    public bool UseTokenAwareSizing { get; set; } = true;
    public int TargetChunkTokens { get; set; } = 3000;
    public int EstimatedCharsPerToken { get; set; } = 4;
    public bool EnableQueryFocusedSecondPass { get; set; } = true;
    public int MaxFocusedChunksPerAttachment { get; set; } = 3;
    public int MinQueryKeywordLength { get; set; } = 4;
    public int MaxChunksPerAttachment { get; set; } = 20;
    public int MaxChunkNoteChars { get; set; } = 1200;
    public int MaxFinalNotesPerAgent { get; set; } = 8;
}
