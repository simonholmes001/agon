using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Agents;

public sealed record GeminiCouncilAgentOptions(
    string AgentId,
    string ApiKey,
    string ModelName,
    int MaxOutputTokens,
    double? Temperature = null);

public class GeminiCouncilAgent(
    HttpClient httpClient,
    GeminiCouncilAgentOptions options,
    ILogger<GeminiCouncilAgent> logger) : ICouncilAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string AgentId { get; } = options.AgentId;
    public string ModelProvider => "gemini";

    public async Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var request = BuildRequest(context);
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{options.ModelName}:generateContent?key={options.ApiKey}";
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorSummary = ProviderErrorSummary.FromResponseBody(responseBody);
                logger.LogError(
                    "Gemini call failed. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} StatusCode={StatusCode} LatencyMs={LatencyMs} CorrelationId={CorrelationId} ErrorSummary={ErrorSummary}",
                    context.SessionId,
                    context.Round,
                    AgentId,
                    options.ModelName,
                    (int)response.StatusCode,
                    startedAt.ElapsedMilliseconds,
                    context.CorrelationId,
                    errorSummary);
                throw new HttpRequestException(
                    $"Gemini request failed with status {(int)response.StatusCode}. Cause: {errorSummary}",
                    null,
                    response.StatusCode);
            }

            var message = ParseOutputText(responseBody);
            var parsed = AgentResponseParser.Parse(message);
            logger.LogInformation(
                "Gemini call succeeded. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                AgentId,
                options.ModelName,
                startedAt.ElapsedMilliseconds,
                context.CorrelationId);
            return new AgentResponse
            {
                Message = parsed.Message,
                Patch = parsed.Patch,
                RawOutput = responseBody
            };
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Gemini call failed unexpectedly. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                AgentId,
                options.ModelName,
                startedAt.ElapsedMilliseconds,
                context.CorrelationId);
            throw;
        }
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(
        AgentContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await RunAsync(context, cancellationToken);
        yield return response.Message;
    }

    private object BuildRequest(AgentContext context)
    {
        var prompt = AgentPromptComposer.ComposePrompt(options.AgentId, context);

        if (options.Temperature.HasValue)
        {
            return new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = options.MaxOutputTokens,
                    temperature = options.Temperature.Value
                }
            };
        }

        return new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = options.MaxOutputTokens
            }
        };
    }

    private static string ParseOutputText(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "No response returned from the model.";
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            return "Model returned an empty response.";
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var texts = parts.EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (texts.Count > 0)
            {
                return string.Join("\n", texts);
            }
        }

        return "Model returned an empty response.";
    }
}
