namespace Agon.Application.Orchestration;

/// <summary>
/// Controls context-window chunk-loop behavior for long attachment analysis.
/// </summary>
public sealed class AttachmentChunkLoopOptions
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
