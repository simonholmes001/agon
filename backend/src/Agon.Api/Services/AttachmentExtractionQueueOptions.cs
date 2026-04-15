namespace Agon.Api.Services;

public sealed class AttachmentExtractionQueueOptions
{
    public int Capacity { get; set; } = 128;
    public int RecoveryBatchSize { get; set; } = 256;
    public int RecoveryIntervalSeconds { get; set; } = 30;
}
