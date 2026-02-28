using System.Globalization;

namespace Agon.Api.Configuration;

/// <summary>
/// Parsed provider configuration with validation status.
/// </summary>
public sealed class ProviderConfiguration
{
    // Defaults
    private const string OpenAiDefaultModel = "gpt-5.2";
    private const string GeminiDefaultModel = "gemini-3.1-pro-preview";
    private const string AnthropicDefaultModel = "claude-opus-4-6";
    private const string TechnicalArchitectTemporaryModelDefault = "gpt-5.2";
    private const int DefaultMaxOutputTokens = 2400;
    private const int DefaultModeratorMaxOutputTokens = 3200;

    // OpenAI
    public string? OpenAiApiKey { get; private init; }
    public string OpenAiModel { get; private init; } = OpenAiDefaultModel;
    public double? OpenAiTemperature { get; private init; }
    public int OpenAiMaxOutputTokens { get; private init; } = DefaultMaxOutputTokens;
    public bool OpenAiTemperatureIsInvalid { get; private init; }
    public bool OpenAiMaxOutputTokensIsInvalid { get; private init; }

    // Gemini
    public string? GeminiApiKey { get; private init; }
    public string GeminiModel { get; private init; } = GeminiDefaultModel;
    public double? GeminiTemperature { get; private init; }
    public int GeminiMaxOutputTokens { get; private init; } = DefaultMaxOutputTokens;
    public bool GeminiTemperatureIsInvalid { get; private init; }
    public bool GeminiMaxOutputTokensIsInvalid { get; private init; }

    // Anthropic
    public string? AnthropicApiKey { get; private init; }
    public string AnthropicModel { get; private init; } = AnthropicDefaultModel;
    public double? AnthropicTemperature { get; private init; }
    public int AnthropicMaxOutputTokens { get; private init; } = DefaultMaxOutputTokens;
    public bool AnthropicTemperatureIsInvalid { get; private init; }
    public bool AnthropicMaxOutputTokensIsInvalid { get; private init; }

    // Technical Architect (temporary OpenAI override)
    public string TechnicalArchitectModel { get; private init; } = TechnicalArchitectTemporaryModelDefault;
    public double? TechnicalArchitectTemperature { get; private init; }
    public int TechnicalArchitectMaxOutputTokens { get; private init; } = DefaultMaxOutputTokens;
    public bool TechnicalArchitectTemperatureIsInvalid { get; private init; }
    public bool TechnicalArchitectMaxOutputTokensIsInvalid { get; private init; }

    // Synthesis
    public int SynthesisMaxOutputTokens { get; private init; } = DefaultModeratorMaxOutputTokens;
    public bool SynthesisMaxOutputTokensIsInvalid { get; private init; }

    /// <summary>
    /// Loads provider configuration from environment variables and IConfiguration.
    /// </summary>
    public static ProviderConfiguration Load(IConfiguration configuration)
    {
        // OpenAI
        var openAiApiKey = GetSetting(configuration, "OPENAI_KEY", "OpenAI:ApiKey");
        var openAiModel = GetSetting(configuration, "OPENAI_MODEL", "OpenAI:Model") ?? OpenAiDefaultModel;
        var openAiTemperatureResult = ParseTemperature(GetSetting(configuration, "OPENAI_TEMPERATURE", "OpenAI:Temperature"));
        var openAiMaxTokensResult = ParseMaxOutputTokens(GetSetting(configuration, "OPENAI_MAX_OUTPUT_TOKENS", "OpenAI:MaxOutputTokens"));

        // Gemini
        var geminiApiKey = GetSetting(configuration, "GEMINI_KEY", "Gemini:ApiKey");
        var geminiModel = GetSetting(configuration, "GEMINI_MODEL", "Gemini:Model") ?? GeminiDefaultModel;
        var geminiTemperatureResult = ParseTemperature(GetSetting(configuration, "GEMINI_TEMPERATURE", "Gemini:Temperature"));
        var geminiMaxTokensResult = ParseMaxOutputTokens(GetSetting(configuration, "GEMINI_MAX_OUTPUT_TOKENS", "Gemini:MaxOutputTokens"));

        // Anthropic
        var anthropicApiKey = GetSetting(configuration, "ANTHROPIC_KEY", "Anthropic:ApiKey")
            ?? GetSetting(configuration, "CLAUDE_KEY", null);
        var anthropicModel = GetSetting(configuration, "ANTHROPIC_MODEL", "Anthropic:Model") ?? AnthropicDefaultModel;
        var anthropicTemperatureResult = ParseTemperature(GetSetting(configuration, "ANTHROPIC_TEMPERATURE", "Anthropic:Temperature"));
        var anthropicMaxTokensResult = ParseMaxOutputTokens(GetSetting(configuration, "ANTHROPIC_MAX_OUTPUT_TOKENS", "Anthropic:MaxOutputTokens"));

        // Technical Architect (temporary)
        var technicalArchitectModel = Environment.GetEnvironmentVariable("TECHNICAL_ARCHITECT_MODEL")
            ?? TechnicalArchitectTemporaryModelDefault;
        var technicalArchitectTemperatureResult = ParseTemperature(
            GetSetting(configuration, "TECHNICAL_ARCHITECT_TEMPERATURE", "TechnicalArchitect:Temperature"));
        var technicalArchitectMaxTokensResult = ParseMaxOutputTokens(
            GetSetting(configuration, "TECHNICAL_ARCHITECT_MAX_OUTPUT_TOKENS", "TechnicalArchitect:MaxOutputTokens"));

        // Synthesis
        var synthesisMaxTokensResult = ParseMaxOutputTokens(
            GetSetting(configuration, "SYNTHESIS_MAX_OUTPUT_TOKENS", "Synthesis:MaxOutputTokens"));

        var openAiMaxTokens = openAiMaxTokensResult.Value ?? DefaultMaxOutputTokens;
        var technicalArchitectMaxTokens = technicalArchitectMaxTokensResult.Value ?? openAiMaxTokens;
        var synthesisMaxTokens = synthesisMaxTokensResult.Value ?? Math.Max(openAiMaxTokens, DefaultModeratorMaxOutputTokens);

        return new ProviderConfiguration
        {
            // OpenAI
            OpenAiApiKey = openAiApiKey,
            OpenAiModel = openAiModel,
            OpenAiTemperature = openAiTemperatureResult.Value,
            OpenAiMaxOutputTokens = openAiMaxTokens,
            OpenAiTemperatureIsInvalid = openAiTemperatureResult.IsInvalid,
            OpenAiMaxOutputTokensIsInvalid = openAiMaxTokensResult.IsInvalid,

            // Gemini
            GeminiApiKey = geminiApiKey,
            GeminiModel = geminiModel,
            GeminiTemperature = geminiTemperatureResult.Value,
            GeminiMaxOutputTokens = geminiMaxTokensResult.Value ?? DefaultMaxOutputTokens,
            GeminiTemperatureIsInvalid = geminiTemperatureResult.IsInvalid,
            GeminiMaxOutputTokensIsInvalid = geminiMaxTokensResult.IsInvalid,

            // Anthropic
            AnthropicApiKey = anthropicApiKey,
            AnthropicModel = anthropicModel,
            AnthropicTemperature = anthropicTemperatureResult.Value,
            AnthropicMaxOutputTokens = anthropicMaxTokensResult.Value ?? DefaultMaxOutputTokens,
            AnthropicTemperatureIsInvalid = anthropicTemperatureResult.IsInvalid,
            AnthropicMaxOutputTokensIsInvalid = anthropicMaxTokensResult.IsInvalid,

            // Technical Architect
            TechnicalArchitectModel = technicalArchitectModel,
            TechnicalArchitectTemperature = technicalArchitectTemperatureResult.Value ?? openAiTemperatureResult.Value,
            TechnicalArchitectMaxOutputTokens = technicalArchitectMaxTokens,
            TechnicalArchitectTemperatureIsInvalid = technicalArchitectTemperatureResult.IsInvalid,
            TechnicalArchitectMaxOutputTokensIsInvalid = technicalArchitectMaxTokensResult.IsInvalid,

            // Synthesis
            SynthesisMaxOutputTokens = synthesisMaxTokens,
            SynthesisMaxOutputTokensIsInvalid = synthesisMaxTokensResult.IsInvalid
        };
    }

    /// <summary>
    /// Logs validation warnings for invalid configuration values.
    /// </summary>
    public void LogValidationWarnings(ILogger logger)
    {
        if (OpenAiTemperatureIsInvalid)
        {
            logger.LogWarning("OPENAI_TEMPERATURE is invalid. Expected decimal between 0 and 2. Falling back to provider default.");
        }

        if (GeminiTemperatureIsInvalid)
        {
            logger.LogWarning("GEMINI_TEMPERATURE is invalid. Expected decimal between 0 and 2. Falling back to provider default.");
        }

        if (AnthropicTemperatureIsInvalid)
        {
            logger.LogWarning("ANTHROPIC_TEMPERATURE is invalid. Expected decimal between 0 and 2. Falling back to provider default.");
        }

        if (TechnicalArchitectTemperatureIsInvalid)
        {
            logger.LogWarning("TECHNICAL_ARCHITECT_TEMPERATURE is invalid. Expected decimal between 0 and 2. Falling back to OpenAI temperature or provider default.");
        }

        if (OpenAiMaxOutputTokensIsInvalid)
        {
            logger.LogWarning("OPENAI_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
        }

        if (GeminiMaxOutputTokensIsInvalid)
        {
            logger.LogWarning("GEMINI_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
        }

        if (AnthropicMaxOutputTokensIsInvalid)
        {
            logger.LogWarning("ANTHROPIC_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
        }

        if (TechnicalArchitectMaxOutputTokensIsInvalid)
        {
            logger.LogWarning("TECHNICAL_ARCHITECT_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
        }

        if (SynthesisMaxOutputTokensIsInvalid)
        {
            logger.LogWarning("SYNTHESIS_MAX_OUTPUT_TOKENS is invalid. Expected integer between 128 and 16384.");
        }
    }

    /// <summary>
    /// Logs warnings for missing API keys.
    /// </summary>
    public void LogMissingApiKeyWarnings(ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(OpenAiApiKey))
        {
            logger.LogWarning("OPENAI_KEY is not configured. openai-backed agents will emit system error messages.");
        }

        if (string.IsNullOrWhiteSpace(GeminiApiKey))
        {
            logger.LogWarning("GEMINI_KEY is not configured. gemini-backed agents will emit system error messages.");
        }

        if (string.IsNullOrWhiteSpace(AnthropicApiKey))
        {
            logger.LogWarning("ANTHROPIC_KEY/CLAUDE_KEY is not configured. anthropic-backed agents will emit system error messages.");
        }
    }

    /// <summary>
    /// Logs the current configuration state.
    /// </summary>
    public void LogConfiguration(ILogger logger)
    {
        const string ProviderDefault = "provider-default";

        logger.LogInformation(
            "Council provider configuration: OpenAI={OpenAiConfigured}, Gemini={GeminiConfigured}, Anthropic={AnthropicConfigured}. " +
            "Temperatures: OpenAI={OpenAiTemperature}, Gemini={GeminiTemperature}, Anthropic={AnthropicTemperature}, TechnicalArchitect={TechnicalArchitectTemperature}. " +
            "MaxOutputTokens: OpenAI={OpenAiTokens}, Gemini={GeminiTokens}, Anthropic={AnthropicTokens}, TechnicalArchitect={TechnicalArchitectTokens}, Synthesis={SynthesisTokens}",
            !string.IsNullOrWhiteSpace(OpenAiApiKey),
            !string.IsNullOrWhiteSpace(GeminiApiKey),
            !string.IsNullOrWhiteSpace(AnthropicApiKey),
            OpenAiTemperature?.ToString(CultureInfo.InvariantCulture) ?? ProviderDefault,
            GeminiTemperature?.ToString(CultureInfo.InvariantCulture) ?? ProviderDefault,
            AnthropicTemperature?.ToString(CultureInfo.InvariantCulture) ?? ProviderDefault,
            TechnicalArchitectTemperature?.ToString(CultureInfo.InvariantCulture) ?? ProviderDefault,
            OpenAiMaxOutputTokens,
            GeminiMaxOutputTokens,
            AnthropicMaxOutputTokens,
            TechnicalArchitectMaxOutputTokens,
            SynthesisMaxOutputTokens);

        logger.LogWarning(
            "Temporary provider override active: technical_architect is mapped to OpenAI model {Model} until DeepSeek billing is restored.",
            TechnicalArchitectModel);
    }

    private static string? GetSetting(IConfiguration configuration, string envVarName, string? configPath)
    {
        var value = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = configuration[envVarName];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (configPath is not null)
        {
            value = configuration[configPath];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static (double? Value, bool IsInvalid) ParseTemperature(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return (null, false);
        }

        var parsed = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature)
            && temperature is >= 0 and <= 2;
        return parsed
            ? (temperature, false)
            : (null, true);
    }

    private static (int? Value, bool IsInvalid) ParseMaxOutputTokens(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return (null, false);
        }

        var parsed = int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens)
            && tokens is >= 128 and <= 16384;
        return parsed
            ? (tokens, false)
            : (null, true);
    }
}
