using Agon.Application.Attachments;
using FluentAssertions;

namespace Agon.Infrastructure.Tests.Attachments;

public class AttachmentRoutingPolicyTests
{
    public static TheoryData<string, string, AttachmentRoutingRoute> KnownContentTypeRoutes =>
        new()
        {
            { "notes.bin", "text/plain", AttachmentRoutingRoute.Text },
            { "payload.bin", "application/json", AttachmentRoutingRoute.Text },
            { "diagram.bin", "image/png", AttachmentRoutingRoute.Image },
            { "scan.bin", "image/jpeg", AttachmentRoutingRoute.Image },
            { "brief.bin", "application/pdf", AttachmentRoutingRoute.Document },
            { "deck.bin", "application/vnd.openxmlformats-officedocument.presentationml.presentation", AttachmentRoutingRoute.Document }
        };

    public static TheoryData<string, AttachmentRoutingRoute> ExtensionFallbackRoutes =>
        new()
        {
            { "draft.md", AttachmentRoutingRoute.Text },
            { "report.csv", AttachmentRoutingRoute.Text },
            { "photo.HEIC", AttachmentRoutingRoute.Image },
            { "slide.PPTX", AttachmentRoutingRoute.Document }
        };

    [Theory]
    [MemberData(nameof(KnownContentTypeRoutes))]
    public void Resolve_KnownContentType_AlwaysUsesExpectedRoute(
        string fileName,
        string contentType,
        AttachmentRoutingRoute expected)
    {
        var route = AttachmentRoutingPolicy.Resolve(fileName, contentType);

        route.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(ExtensionFallbackRoutes))]
    public void Resolve_UnknownOrGenericContentType_UsesExtensionFallback(
        string fileName,
        AttachmentRoutingRoute expected)
    {
        AttachmentRoutingPolicy.Resolve(fileName, "application/octet-stream").Should().Be(expected);
        AttachmentRoutingPolicy.Resolve(fileName, "application/x-unknown").Should().Be(expected);
        AttachmentRoutingPolicy.Resolve(fileName, string.Empty).Should().Be(expected);
    }

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

    [Fact]
    public void Resolve_UnknownContentTypeWithoutExtension_ReturnsUnsupported()
    {
        var route = AttachmentRoutingPolicy.Resolve("README", "application/octet-stream");

        route.Should().Be(AttachmentRoutingRoute.Unsupported);
    }
}
