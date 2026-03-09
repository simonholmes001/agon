namespace Agon.Api.Configuration;

/// <summary>
/// LLM provider configuration loaded from appsettings.json
/// </summary>
public sealed class LlmConfiguration
{
    public const string SectionName = "LLM";

    public OpenAIConfig OpenAI { get; set; } = new();
    public AnthropicConfig Anthropic { get; set; } = new();
    public GoogleConfig Google { get; set; } = new();
    public DeepSeekConfig DeepSeek { get; set; } = new();
}

public sealed class OpenAIConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
}

public sealed class AnthropicConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-opus-4-6";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
}

public sealed class GoogleConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.0-flash-thinking-exp-01-21";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
}

public sealed class DeepSeekConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "deepseek-chat";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
}
