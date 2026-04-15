using Agon.Application.Models;

namespace Agon.Api.Services;

public interface IAttachmentExtractionQueue
{
    bool TryQueue(SessionAttachment attachment);
    ValueTask<SessionAttachment> DequeueAsync(CancellationToken cancellationToken);
}
