using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Agents;

public sealed record DeepSeekCouncilAgentOptions(
    string AgentId,
    string ApiKey,
    string ModelName,
    int MaxOutputTokens,
    double? Temperature = null);

public class DeepSeekCouncilAgent(
    HttpClient httpClient,
    DeepSeekCouncilAgentOptions options,
    ILogger<DeepSeekCouncilAgent> logger) : ICouncilAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string AgentId { get; } = options.AgentId;
    public string ModelProvider => "deepseek";

    public async Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var request = BuildRequest(context);
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions")
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorSummary = ProviderErrorSummary.FromResponseBody(responseBody);
                logger.LogError(
                    "DeepSeek call failed. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} StatusCode={StatusCode} LatencyMs={LatencyMs} CorrelationId={CorrelationId} ErrorSummary={ErrorSummary}",
                    context.SessionId,
                    context.Round,
                    AgentId,
                    options.ModelName,
                    (int)response.StatusCode,
                    startedAt.ElapsedMilliseconds,
                    context.CorrelationId,
                    errorSummary);
                throw new HttpRequestException(
                    $"DeepSeek request failed with status {(int)response.StatusCode}. Cause: {errorSummary}",
                    null,
                    response.StatusCode);
            }

            var message = ParseOutputText(responseBody);
            var parsed = AgentResponseParser.Parse(message);
            logger.LogInformation(
                "DeepSeek call succeeded. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
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
                "DeepSeek call failed unexpectedly. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
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
                model = options.ModelName,
                max_tokens = options.MaxOutputTokens,
                temperature = options.Temperature.Value,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };
        }

        return new
        {
            model = options.ModelName,
            max_tokens = options.MaxOutputTokens,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
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
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array)
        {
            return "Model returned an empty response.";
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = content.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return "Model returned an empty response.";
    }
}
