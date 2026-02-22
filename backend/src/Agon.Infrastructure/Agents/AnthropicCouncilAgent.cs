using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Agon.Application.Interfaces;
using Agon.Application.Orchestration;
using Microsoft.Extensions.Logging;

namespace Agon.Infrastructure.Agents;

public sealed record AnthropicCouncilAgentOptions(
    string AgentId,
    string ApiKey,
    string ModelName,
    int MaxOutputTokens,
    double? Temperature = null);

public class AnthropicCouncilAgent(
    HttpClient httpClient,
    AnthropicCouncilAgentOptions options,
    ILogger<AnthropicCouncilAgent> logger) : ICouncilAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string AgentId { get; } = options.AgentId;
    public string ModelProvider => "anthropic";

    public async Task<AgentResponse> RunAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        var request = BuildRequest(context);
        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = content
        };
        httpRequest.Headers.Add("x-api-key", options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorSummary = ProviderErrorSummary.FromResponseBody(responseBody);
                logger.LogError(
                    "Anthropic call failed. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} StatusCode={StatusCode} LatencyMs={LatencyMs} CorrelationId={CorrelationId} ErrorSummary={ErrorSummary}",
                    context.SessionId,
                    context.Round,
                    AgentId,
                    options.ModelName,
                    (int)response.StatusCode,
                    startedAt.ElapsedMilliseconds,
                    context.CorrelationId,
                    errorSummary);
                throw new HttpRequestException(
                    $"Anthropic request failed with status {(int)response.StatusCode}. Cause: {errorSummary}",
                    null,
                    response.StatusCode);
            }

            var message = ParseOutputText(responseBody);
            logger.LogInformation(
                "Anthropic call succeeded. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
                context.SessionId,
                context.Round,
                AgentId,
                options.ModelName,
                startedAt.ElapsedMilliseconds,
                context.CorrelationId);
            return new AgentResponse
            {
                Message = message,
                Patch = null,
                RawOutput = responseBody
            };
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Anthropic call failed unexpectedly. SessionId={SessionId} Round={Round} AgentId={AgentId} Model={Model} LatencyMs={LatencyMs} CorrelationId={CorrelationId}",
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
        var prompt = $"""
            You are agent '{AgentId}'.
            Session: {context.SessionId}
            Round: {context.Round}
            Phase: {context.Phase}
            FrictionLevel: {context.FrictionLevel}
            CoreIdea: {context.TruthMap.CoreIdea}

            Provide a concise analysis (max 120 words) with actionable recommendations.
            """;

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
        if (!root.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return "Model returned an empty response.";
        }

        var texts = content.EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Where(item => item.TryGetProperty("text", out _))
            .Select(item => item.GetProperty("text").GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return texts.Count > 0
            ? string.Join("\n", texts)
            : "Model returned an empty response.";
    }
}
