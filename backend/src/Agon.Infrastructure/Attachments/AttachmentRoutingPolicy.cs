namespace Agon.Infrastructure.Attachments;

public enum AttachmentRoutingRoute
{
    Unsupported = 0,
    Image = 1,
    Document = 2,
    Text = 3
}

public static class AttachmentRoutingPolicy
{
    private static readonly HashSet<string> TextContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "application/xml",
        "application/x-yaml",
        "application/yaml",
        "text/csv",
        "application/csv",
        "application/x-www-form-urlencoded",
        "application/javascript",
        "application/x-javascript",
        "application/typescript",
        "application/sql",
        "application/rtf"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".yaml", ".yml", ".csv", ".xml", ".html", ".htm",
        ".log", ".ini", ".cfg", ".conf", ".toml", ".sql", ".ts", ".js", ".tsx", ".jsx",
        ".cs", ".py", ".java", ".go", ".rb", ".php", ".ps1", ".sh", ".bat", ".env", ".rtf"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
    };

    private static readonly HashSet<string> DocumentContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif", ".jfif"
    };

    private static readonly HashSet<string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/pjpeg",
        "image/gif",
        "image/bmp",
        "image/webp",
        "image/tiff",
        "image/heic",
        "image/heif"
    };

    private static readonly HashSet<string> GenericBinaryContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/octet-stream",
        "binary/octet-stream"
    };

    public static AttachmentRoutingRoute Resolve(string fileName, string normalizedContentType)
    {
        if (TryResolveByKnownContentType(normalizedContentType, out var contentTypeRoute))
        {
            return contentTypeRoute;
        }

        if (TryResolveByExtension(fileName, out var extensionRoute))
        {
            return extensionRoute;
        }

        return AttachmentRoutingRoute.Unsupported;
    }

    private static bool TryResolveByKnownContentType(string normalizedContentType, out AttachmentRoutingRoute route)
    {
        route = AttachmentRoutingRoute.Unsupported;
        if (string.IsNullOrWhiteSpace(normalizedContentType))
        {
            return false;
        }

        if (normalizedContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || TextContentTypes.Contains(normalizedContentType))
        {
            route = AttachmentRoutingRoute.Text;
            return true;
        }

        if (ImageContentTypes.Contains(normalizedContentType))
        {
            route = AttachmentRoutingRoute.Image;
            return true;
        }

        if (DocumentContentTypes.Contains(normalizedContentType))
        {
            route = AttachmentRoutingRoute.Document;
            return true;
        }

        return false;
    }

    private static bool TryResolveByExtension(string fileName, out AttachmentRoutingRoute route)
    {
        route = AttachmentRoutingRoute.Unsupported;
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        if (ImageExtensions.Contains(extension))
        {
            route = AttachmentRoutingRoute.Image;
            return true;
        }

        if (DocumentExtensions.Contains(extension))
        {
            route = AttachmentRoutingRoute.Document;
            return true;
        }

        if (TextExtensions.Contains(extension))
        {
            route = AttachmentRoutingRoute.Text;
            return true;
        }

        return false;
    }
}
