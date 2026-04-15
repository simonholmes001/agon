using Agon.Application.Models;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Agon.Api.Services;

public sealed class AttachmentExtractionQueue : IAttachmentExtractionQueue
{
    private readonly Channel<SessionAttachment> _channel;
    private readonly ConcurrentDictionary<Guid, byte> _pendingIds = new();

    public AttachmentExtractionQueue(AttachmentExtractionQueueOptions options)
    {
        var capacity = Math.Max(1, options.Capacity);
        _channel = Channel.CreateBounded<SessionAttachment>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryQueue(SessionAttachment attachment)
    {
        if (!_pendingIds.TryAdd(attachment.AttachmentId, 0))
        {
            return true;
        }

        if (_channel.Writer.TryWrite(attachment))
        {
            return true;
        }

        _pendingIds.TryRemove(attachment.AttachmentId, out _);
        return false;
    }

    public async ValueTask<SessionAttachment> DequeueAsync(CancellationToken cancellationToken)
    {
        var attachment = await _channel.Reader.ReadAsync(cancellationToken);
        _pendingIds.TryRemove(attachment.AttachmentId, out _);
        return attachment;
    }
}
