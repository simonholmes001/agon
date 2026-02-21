using Agon.Api.Configuration;
using FluentAssertions;

namespace Agon.Api.Tests.Configuration;

public class DotEnvLoaderTests
{
    [Fact]
    public void ParseLines_ParsesQuotedAndExportedValues()
    {
        var lines = new[]
        {
            "  # comment",
            "OPENAI_KEY=\"openai-value\"",
            "export GEMINI_KEY='gemini-value'",
            "DEEPSEEK_KEY=deepseek-value",
            "INVALID_LINE"
        };

        var parsed = DotEnvLoader.ParseLines(lines);

        parsed.Should().Contain(new KeyValuePair<string, string>("OPENAI_KEY", "openai-value"));
        parsed.Should().Contain(new KeyValuePair<string, string>("GEMINI_KEY", "gemini-value"));
        parsed.Should().Contain(new KeyValuePair<string, string>("DEEPSEEK_KEY", "deepseek-value"));
        parsed.Should().HaveCount(3);
    }

    [Fact]
    public void Load_LoadsNearestDotEnv_AndSkipsExistingEnvironmentVariables()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"agon-dotenv-{Guid.NewGuid():N}");
        var nestedDirectory = Path.Combine(rootDirectory, "nested", "deeper");
        Directory.CreateDirectory(nestedDirectory);

        var envFilePath = Path.Combine(rootDirectory, ".env");
        File.WriteAllText(
            envFilePath,
            """
            OPENAI_KEY="from-file-openai"
            GEMINI_KEY=from-file-gemini
            """);

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OPENAI_KEY"] = "existing-openai"
        };

        string? GetEnvironmentVariable(string key) =>
            environment.TryGetValue(key, out var value) ? value : null;

        void SetEnvironmentVariable(string key, string value) => environment[key] = value;

        try
        {
            var result = DotEnvLoader.Load(nestedDirectory, GetEnvironmentVariable, SetEnvironmentVariable);

            result.FilePath.Should().Be(envFilePath);
            result.LoadedCount.Should().Be(1);
            result.SkippedExistingCount.Should().Be(1);
            environment["OPENAI_KEY"].Should().Be("existing-openai");
            environment["GEMINI_KEY"].Should().Be("from-file-gemini");
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsEmptyResult_WhenDotEnvFileIsNotFound()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"agon-dotenv-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var result = DotEnvLoader.Load(directory);

            result.FilePath.Should().BeNull();
            result.LoadedCount.Should().Be(0);
            result.SkippedExistingCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
