using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Agents;

public sealed record OpenAiCouncilAgentOptions(
    string AgentId,
    string ApiKey,
    string ModelName,
    int MaxOutputTokens,
    double? Temperature = null);

public class OpenAiCouncilAgent(
    HttpClient httpClient,
    OpenAiCouncilAgentOptions options,
    ILogger<OpenAiCouncilAgent> logger) : ICouncilAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string AgentId { get; } = options.AgentId;
    public string ModelProvider => "openai";

    public async Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var request = BuildRequest(context);
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorSummary = ProviderErrorSummary.FromResponseBody(responseBody);
                logger.LogError(
                    "OpenAI call failed. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} StatusCode={StatusCode} LatencyMs={LatencyMs} CorrelationId={CorrelationId} ErrorSummary={ErrorSummary}",
                    context.SessionId,
                    context.Round,
                    AgentId,
                    options.ModelName,
                    (int)response.StatusCode,
                    startedAt.ElapsedMilliseconds,
                    context.CorrelationId,
                    errorSummary);

                throw new HttpRequestException(
                    $"OpenAI request failed with status {(int)response.StatusCode}. Cause: {errorSummary}",
                    null,
                    response.StatusCode);
            }

            var message = ParseOutputText(responseBody);
            var parsed = AgentResponseParser.Parse(message);
            logger.LogInformation(
                "OpenAI call succeeded. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
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
                "OpenAI call failed unexpectedly. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
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
        var input = AgentPromptComposer.ComposePrompt(options.AgentId, context);

        if (options.Temperature.HasValue)
        {
            return new
            {
                model = options.ModelName,
                max_output_tokens = options.MaxOutputTokens,
                temperature = options.Temperature.Value,
                input
            };
        }

        return new
        {
            model = options.ModelName,
            max_output_tokens = options.MaxOutputTokens,
            input
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

        if (root.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            var value = outputText.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        if (root.TryGetProperty("output", out var output)
            && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String)
                    {
                        var value = text.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value!;
                        }
                    }
                }
            }
        }

        return "Model returned an empty response.";
    }
}
