using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agon.Application.Attachments;
using Agon.Application.Interfaces;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Attachments;

public sealed class AttachmentTextExtractor : IAttachmentTextExtractor
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

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
        var route = AttachmentRoutingPolicy.Resolve(fileName, normalizedContentType);
        if (route == AttachmentRoutingRoute.Image)
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

        if (route == AttachmentRoutingRoute.Document)
        {
            var documentResult = await ExtractWithDocumentIntelligenceAsync(content, cancellationToken);
            if (!string.IsNullOrWhiteSpace(documentResult))
            {
                return NormalizeText(documentResult);
            }
        }

        if (route == AttachmentRoutingRoute.Text)
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

        var client = _httpClientFactory.CreateClient("attachment-extraction");
        var startSend = await SendWithTransientRetryAsync(
            client,
            async ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, analyzeUrl)
                {
                    Content = JsonContent.Create(requestBody)
                };
                await ApplyDocumentIntelligenceAuthAsync(request, ct);
                return request;
            },
            "document-intelligence-analyze-start",
            cancellationToken);

        using var startResponse = startSend.Response;
        if (startResponse is null)
        {
            if (startSend.FailureKind == RetryFailureKind.Timeout)
            {
                throw new TaskCanceledException("Document Intelligence analyze request timed out after retries.");
            }

            if (startSend.FailureKind == RetryFailureKind.HttpException)
            {
                throw new HttpRequestException("Document Intelligence analyze request failed after retries.");
            }

            return null;
        }

        if (startResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            var immediatePayload = await startResponse.Content.ReadAsStringAsync(cancellationToken);
            return ParseDocumentIntelligenceContent(immediatePayload);
        }

        if (startResponse.StatusCode != HttpStatusCode.Accepted)
        {
            var errorPayload = await startResponse.Content.ReadAsStringAsync(cancellationToken);
            if (IsTransientStatusCode(startResponse.StatusCode))
            {
                throw new HttpRequestException($"Document Intelligence analyze request failed with transient status {(int)startResponse.StatusCode}.");
            }

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

            var pollSend = await SendWithTransientRetryAsync(
                client,
                async ct =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, operationLocation);
                    await ApplyDocumentIntelligenceAuthAsync(request, ct);
                    return request;
                },
                "document-intelligence-analyze-poll",
                cancellationToken);

            using var pollResponse = pollSend.Response;
            if (pollResponse is null)
            {
                if (pollSend.FailureKind == RetryFailureKind.Timeout)
                {
                    throw new TaskCanceledException("Document Intelligence polling timed out after retries.");
                }

                if (pollSend.FailureKind == RetryFailureKind.HttpException)
                {
                    throw new HttpRequestException("Document Intelligence polling failed after retries.");
                }

                return null;
            }

            var pollPayload = await pollResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!pollResponse.IsSuccessStatusCode)
            {
                if (IsTransientStatusCode(pollResponse.StatusCode))
                {
                    throw new HttpRequestException($"Document Intelligence poll failed with transient status {(int)pollResponse.StatusCode}.");
                }

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
        throw new TaskCanceledException("Document Intelligence analysis timed out while polling.");
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

        var client = _httpClientFactory.CreateClient("attachment-extraction");
        var sendResult = await SendWithTransientRetryAsync(
            client,
            _ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
                {
                    Content = JsonContent.Create(requestPayload)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiVision.ApiKey);
                return Task.FromResult(request);
            },
            $"openai-vision-{model}",
            cancellationToken);

        using var response = sendResult.Response;
        if (response is null)
        {
            return new OpenAiVisionAttemptResult(null, null);
        }

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

    private async Task<HttpRetrySendResult> SendWithTransientRetryAsync(
        HttpClient client,
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        string operationName,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.TransientRetry.MaxAttempts);
        var baseDelayMs = Math.Max(1, _options.TransientRetry.BaseDelayMs);
        var maxDelayMs = Math.Max(baseDelayMs, _options.TransientRetry.MaxDelayMs);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = await requestFactory(cancellationToken);
            try
            {
                var response = await client.SendAsync(request, cancellationToken);
                if (IsTransientStatusCode(response.StatusCode) && attempt < maxAttempts)
                {
                    _logger.LogInformation(
                        "Transient status during {Operation}. Status={StatusCode}, Attempt={Attempt}/{MaxAttempts}. Retrying.",
                        operationName,
                        (int)response.StatusCode,
                        attempt,
                        maxAttempts);
                    response.Dispose();
                    await Task.Delay(ComputeBackoffDelay(attempt, baseDelayMs, maxDelayMs), cancellationToken);
                    continue;
                }

                return new HttpRetrySendResult(response, RetryFailureKind.None);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < maxAttempts)
                {
                    _logger.LogInformation(
                        ex,
                        "Transient HTTP exception during {Operation}. Attempt={Attempt}/{MaxAttempts}. Retrying.",
                        operationName,
                        attempt,
                        maxAttempts);
                    await Task.Delay(ComputeBackoffDelay(attempt, baseDelayMs, maxDelayMs), cancellationToken);
                    continue;
                }

                _logger.LogWarning(
                    ex,
                    "HTTP exception during {Operation} after {Attempts} attempts.",
                    operationName,
                    maxAttempts);
                return new HttpRetrySendResult(null, RetryFailureKind.HttpException);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < maxAttempts)
                {
                    _logger.LogInformation(
                        ex,
                        "Transient timeout during {Operation}. Attempt={Attempt}/{MaxAttempts}. Retrying.",
                        operationName,
                        attempt,
                        maxAttempts);
                    await Task.Delay(ComputeBackoffDelay(attempt, baseDelayMs, maxDelayMs), cancellationToken);
                    continue;
                }

                _logger.LogWarning(
                    ex,
                    "Timeout during {Operation} after {Attempts} attempts.",
                    operationName,
                    maxAttempts);
                return new HttpRetrySendResult(null, RetryFailureKind.Timeout);
            }
        }

        return new HttpRetrySendResult(null, RetryFailureKind.None);
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        var statusCodeInt = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCodeInt == 429
            || statusCodeInt >= 500;
    }

    private static TimeSpan ComputeBackoffDelay(int attempt, int baseDelayMs, int maxDelayMs)
    {
        var exponent = Math.Max(0, attempt - 1);
        var delayMs = (int)Math.Min(maxDelayMs, baseDelayMs * Math.Pow(2, exponent));
        return TimeSpan.FromMilliseconds(Math.Max(1, delayMs));
    }

    private enum RetryFailureKind
    {
        None = 0,
        HttpException = 1,
        Timeout = 2
    }

    private readonly record struct HttpRetrySendResult(
        HttpResponseMessage? Response,
        RetryFailureKind FailureKind);

    private readonly record struct OpenAiVisionAttemptResult(
        string? Text,
        HttpStatusCode? StatusCode);
}
