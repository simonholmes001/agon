using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agon.Application.Interfaces;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Attachments;

public sealed class AttachmentTextExtractor : IAttachmentTextExtractor
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

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
    private static readonly Regex ModelRefusalRegex = new(
        @"\b(i\s*(?:am|'m)\s*unable\s*to\s*(?:assist|help)|i\s*cannot\s*(?:assist|help)|i\s*can'?t\s*(?:assist|help)|can'?t\s*access\s*(?:the\s*)?(?:attached\s*)?(?:image|file)|unable\s*to\s*access\s*(?:the\s*)?(?:image|attachment))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AttachmentExtractionOptions _options;
    private readonly ILogger<AttachmentTextExtractor> _logger;
    private readonly TokenCredential _tokenCredential;

    public AttachmentTextExtractor(
        IHttpClientFactory httpClientFactory,
        AttachmentExtractionOptions options,
        ILogger<AttachmentTextExtractor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _tokenCredential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        });
    }

    public async Task<string?> ExtractAsync(
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
        {
            return null;
        }

        var normalizedContentType = NormalizeContentType(contentType);

        if (IsImage(fileName, normalizedContentType))
        {
            var visionResult = await ExtractWithOpenAiVisionAsync(content, normalizedContentType, cancellationToken);
            var normalizedVision = NormalizeText(visionResult);
            if (!string.IsNullOrWhiteSpace(normalizedVision))
            {
                return normalizedVision;
            }

            var imageOcrResult = await ExtractWithDocumentIntelligenceAsync(content, cancellationToken);
            return NormalizeText(imageOcrResult);
        }

        if (IsDocument(fileName, normalizedContentType))
        {
            var documentResult = await ExtractWithDocumentIntelligenceAsync(content, cancellationToken);
            if (!string.IsNullOrWhiteSpace(documentResult))
            {
                return NormalizeText(documentResult);
            }
        }

        if (IsTextLike(fileName, normalizedContentType))
        {
            return NormalizeText(TryExtractUtf8(content));
        }

        return null;
    }

    private async Task<string?> ExtractWithDocumentIntelligenceAsync(byte[] content, CancellationToken cancellationToken)
    {
        if (!_options.DocumentIntelligence.Enabled || string.IsNullOrWhiteSpace(_options.DocumentIntelligence.Endpoint))
        {
            return null;
        }

        var endpoint = _options.DocumentIntelligence.Endpoint.Trim().TrimEnd('/');
        var modelId = string.IsNullOrWhiteSpace(_options.DocumentIntelligence.ModelId)
            ? "prebuilt-layout"
            : _options.DocumentIntelligence.ModelId.Trim();
        var apiVersion = string.IsNullOrWhiteSpace(_options.DocumentIntelligence.ApiVersion)
            ? "2024-11-30"
            : _options.DocumentIntelligence.ApiVersion.Trim();

        var analyzeUrl = $"{endpoint}/documentintelligence/documentModels/{Uri.EscapeDataString(modelId)}:analyze?api-version={Uri.EscapeDataString(apiVersion)}";
        var requestBody = new { base64Source = Convert.ToBase64String(content) };

        using var startRequest = new HttpRequestMessage(HttpMethod.Post, analyzeUrl)
        {
            Content = JsonContent.Create(requestBody)
        };

        await ApplyDocumentIntelligenceAuthAsync(startRequest, cancellationToken);

        var client = _httpClientFactory.CreateClient("attachment-extraction");
        using var startResponse = await client.SendAsync(startRequest, cancellationToken);

        if (startResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            var immediatePayload = await startResponse.Content.ReadAsStringAsync(cancellationToken);
            return ParseDocumentIntelligenceContent(immediatePayload);
        }

        if (startResponse.StatusCode != HttpStatusCode.Accepted)
        {
            var errorPayload = await startResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Document Intelligence analyze request failed. Status={StatusCode}, Payload={Payload}",
                (int)startResponse.StatusCode,
                Truncate(errorPayload, 600));
            return null;
        }

        var operationLocation = startResponse.Headers.Location?.ToString();
        if (string.IsNullOrWhiteSpace(operationLocation) &&
            startResponse.Headers.TryGetValues("Operation-Location", out var operationValues))
        {
            operationLocation = operationValues.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(operationLocation))
        {
            _logger.LogWarning("Document Intelligence analyze response did not include Operation-Location.");
            return null;
        }

        for (var attempt = 0; attempt < Math.Max(1, _options.DocumentIntelligence.MaxPollAttempts); attempt++)
        {
            await Task.Delay(Math.Max(250, _options.DocumentIntelligence.PollIntervalMs), cancellationToken);

            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, operationLocation);
            await ApplyDocumentIntelligenceAuthAsync(pollRequest, cancellationToken);

            using var pollResponse = await client.SendAsync(pollRequest, cancellationToken);
            var pollPayload = await pollResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!pollResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Document Intelligence poll request failed. Status={StatusCode}, Payload={Payload}",
                    (int)pollResponse.StatusCode,
                    Truncate(pollPayload, 600));
                return null;
            }

            var status = ParseDocumentIntelligenceStatus(pollPayload);
            if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return ParseDocumentIntelligenceContent(pollPayload);
            }

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Document Intelligence analysis failed. Payload={Payload}", Truncate(pollPayload, 600));
                return null;
            }
        }

        _logger.LogWarning("Document Intelligence analysis timed out after {Attempts} polling attempts.", _options.DocumentIntelligence.MaxPollAttempts);
        return null;
    }

    private async Task ApplyDocumentIntelligenceAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.DocumentIntelligence.ApiKey))
        {
            request.Headers.Remove("Ocp-Apim-Subscription-Key");
            request.Headers.Add("Ocp-Apim-Subscription-Key", _options.DocumentIntelligence.ApiKey);
            return;
        }

        if (!_options.DocumentIntelligence.UseManagedIdentity)
        {
            return;
        }

        var token = await _tokenCredential.GetTokenAsync(new TokenRequestContext([CognitiveServicesScope]), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private async Task<string?> ExtractWithOpenAiVisionAsync(
        byte[] content,
        string normalizedContentType,
        CancellationToken cancellationToken)
    {
        if (!_options.OpenAiVision.Enabled || !IsConfiguredSecret(_options.OpenAiVision.ApiKey))
        {
            return null;
        }

        if (content.Length > Math.Max(1024, _options.OpenAiVision.MaxImageBytes))
        {
            _logger.LogInformation(
                "Skipping OpenAI vision extraction for image: payload exceeds configured max bytes ({MaxBytes}).",
                _options.OpenAiVision.MaxImageBytes);
            return null;
        }

        var primaryModel = string.IsNullOrWhiteSpace(_options.OpenAiVision.Model)
            ? "gpt-4o-mini"
            : _options.OpenAiVision.Model.Trim();
        var fallbackModel = string.IsNullOrWhiteSpace(_options.OpenAiVision.FallbackModel)
            ? string.Empty
            : _options.OpenAiVision.FallbackModel.Trim();

        var primaryAttempt = await TryExtractWithOpenAiModelAsync(
            content,
            normalizedContentType,
            primaryModel,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(primaryAttempt.Text))
        {
            return primaryAttempt.Text;
        }

        var shouldTryFallback =
            !string.IsNullOrWhiteSpace(fallbackModel)
            && !string.Equals(primaryModel, fallbackModel, StringComparison.OrdinalIgnoreCase)
            && primaryAttempt.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound;

        if (!shouldTryFallback)
        {
            return null;
        }

        _logger.LogInformation(
            "Retrying OpenAI vision extraction with fallback model {FallbackModel} after primary model {PrimaryModel} failed with status {StatusCode}.",
            fallbackModel,
            primaryModel,
            (int)primaryAttempt.StatusCode!.Value);

        var fallbackAttempt = await TryExtractWithOpenAiModelAsync(
            content,
            normalizedContentType,
            fallbackModel,
            cancellationToken);

        return fallbackAttempt.Text;
    }

    private async Task<OpenAiVisionAttemptResult> TryExtractWithOpenAiModelAsync(
        byte[] content,
        string normalizedContentType,
        string model,
        CancellationToken cancellationToken)
    {
        var dataUrl = $"data:{(string.IsNullOrWhiteSpace(normalizedContentType) ? "application/octet-stream" : normalizedContentType)};base64,{Convert.ToBase64String(content)}";
        var requestPayload = new
        {
            model,
            temperature = 0,
            max_tokens = Math.Max(256, _options.OpenAiVision.MaxTokens),
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You extract text and salient structured details from user-provided images for product and engineering discussions. Return plain text only."
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = "Describe what is visible in the image in plain text. Include readable text, objects, scene, and key details. If there is little/no text, still provide a concise scene description."
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = dataUrl,
                                detail = _options.OpenAiVision.Detail
                            }
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = JsonContent.Create(requestPayload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiVision.ApiKey);

        var client = _httpClientFactory.CreateClient("attachment-extraction");
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OpenAI vision extraction failed for model {Model}. Status={StatusCode}, Payload={Payload}",
                model,
                (int)response.StatusCode,
                Truncate(payload, 600));
            return new OpenAiVisionAttemptResult(null, response.StatusCode);
        }

        return new OpenAiVisionAttemptResult(ParseOpenAiContent(payload), response.StatusCode);
    }

    private static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        return contentType.Split(';')[0].Trim();
    }

    private static bool IsConfiguredSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return !(trimmed.StartsWith("${", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal));
    }

    private static bool IsImage(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            ImageContentTypes.Contains(contentType))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension) && ImageExtensions.Contains(extension);
    }

    private static bool IsDocument(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            DocumentContentTypes.Contains(contentType))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension) && DocumentExtensions.Contains(extension);
    }

    private static bool IsTextLike(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                TextContentTypes.Contains(contentType))
            {
                return true;
            }
        }

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension) && TextExtensions.Contains(extension);
    }

    private static string? TryExtractUtf8(byte[] content)
    {
        try
        {
            return Encoding.UTF8.GetString(content);
        }
        catch
        {
            return null;
        }
    }

    private string? NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = Regex.Replace(normalized, @"[\u0000-\u0008\u000B\u000C\u000E-\u001F]", " ").Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (ModelRefusalRegex.IsMatch(normalized))
        {
            return null;
        }

        if (_options.MaxExtractedTextChars > 0 && normalized.Length > _options.MaxExtractedTextChars)
        {
            normalized = normalized[.._options.MaxExtractedTextChars];
        }

        return normalized;
    }

    private static string? ParseOpenAiContent(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message))
            {
                return null;
            }

            if (!message.TryGetProperty("content", out var content))
            {
                return ParseResponsesApiContent(document.RootElement);
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                var parts = content.EnumerateArray()
                    .Select(ExtractContentText)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .OfType<string>();

                return string.Join("\n", parts);
            }

            if (content.ValueKind == JsonValueKind.Object)
            {
                return ExtractContentText(content);
            }

            return ParseResponsesApiContent(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseResponsesApiContent(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var segments = new List<string>();
        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                var text = ExtractContentText(contentItem);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(text);
                }
            }
        }

        return segments.Count == 0 ? null : string.Join("\n", segments);
    }

    private static string? ExtractContentText(JsonElement contentItem)
    {
        if (contentItem.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (contentItem.TryGetProperty("text", out var textProp))
        {
            if (textProp.ValueKind == JsonValueKind.String)
            {
                return textProp.GetString();
            }

            if (textProp.ValueKind == JsonValueKind.Object &&
                textProp.TryGetProperty("value", out var valueProp) &&
                valueProp.ValueKind == JsonValueKind.String)
            {
                return valueProp.GetString();
            }
        }

        if (contentItem.TryGetProperty("content", out var contentProp) &&
            contentProp.ValueKind == JsonValueKind.String)
        {
            return contentProp.GetString();
        }

        return null;
    }

    private static string? ParseDocumentIntelligenceStatus(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("status", out var statusElement) ||
                statusElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return statusElement.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseDocumentIntelligenceContent(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("analyzeResult", out var analyzeResult) &&
                analyzeResult.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind == JsonValueKind.String)
            {
                return contentElement.GetString();
            }

            if (document.RootElement.TryGetProperty("content", out var fallbackContent) &&
                fallbackContent.ValueKind == JsonValueKind.String)
            {
                return fallbackContent.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    private readonly record struct OpenAiVisionAttemptResult(
        string? Text,
        HttpStatusCode? StatusCode);
}
