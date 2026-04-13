using Agon.Infrastructure.Attachments;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Attachments;

public class AttachmentRoutingPolicyTests
{
    [Fact]
    public void Resolve_KnownTextContentType_PrioritizesOverDocumentExtension()
    {
        var route = AttachmentRoutingPolicy.Resolve("spec.pdf", "text/plain");
        route.Should().Be(AttachmentRoutingRoute.Text);
    }

    [Fact]
    public void Resolve_KnownImageContentType_PrioritizesOverTextExtension()
    {
        var route = AttachmentRoutingPolicy.Resolve("note.md", "image/png");
        route.Should().Be(AttachmentRoutingRoute.Image);
    }

    [Fact]
    public void Resolve_GenericBinaryContentType_FallsBackToExtension()
    {
        var route = AttachmentRoutingPolicy.Resolve("slide.pptx", "application/octet-stream");
        route.Should().Be(AttachmentRoutingRoute.Document);
    }

    [Fact]
    public void Resolve_UnknownContentTypeAndExtension_ReturnsUnsupported()
    {
        var route = AttachmentRoutingPolicy.Resolve("archive.xyz", "application/x-unknown");
        route.Should().Be(AttachmentRoutingRoute.Unsupported);
    }
}
