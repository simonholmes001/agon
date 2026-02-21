namespace Agon.Api.Configuration;

public sealed record DotEnvLoadResult(string? FilePath, int LoadedCount, int SkippedExistingCount);

public static class DotEnvLoader
{
    public static DotEnvLoadResult Load(
        string? startDirectory,
        Func<string, string?>? getEnvironmentVariable = null,
        Action<string, string>? setEnvironmentVariable = null)
    {
        var getEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var setEnv = setEnvironmentVariable ?? ((key, value) => Environment.SetEnvironmentVariable(key, value));
        var filePath = FindNearestDotEnv(startDirectory);
        if (filePath is null)
        {
            return new DotEnvLoadResult(null, 0, 0);
        }

        var loaded = 0;
        var skippedExisting = 0;
        var lines = File.ReadAllLines(filePath);

        foreach (var pair in ParseLines(lines))
        {
            var existing = getEnv(pair.Key);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                skippedExisting++;
                continue;
            }

            setEnv(pair.Key, pair.Value);
            loaded++;
        }

        return new DotEnvLoadResult(filePath, loaded, skippedExisting);
    }

    public static string? FindNearestDotEnv(string? startDirectory)
    {
        var current = string.IsNullOrWhiteSpace(startDirectory)
            ? new DirectoryInfo(Directory.GetCurrentDirectory())
            : new DirectoryInfo(startDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    public static IReadOnlyList<KeyValuePair<string, string>> ParseLines(IEnumerable<string> lines)
    {
        var result = new List<KeyValuePair<string, string>>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].Trim();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim();
            result.Add(new KeyValuePair<string, string>(key, StripWrappingQuotes(value)));
        }

        return result;
    }

    private static string StripWrappingQuotes(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var isDoubleQuoted = value[0] == '"' && value[^1] == '"';
        var isSingleQuoted = value[0] == '\'' && value[^1] == '\'';
        return isDoubleQuoted || isSingleQuoted
            ? value[1..^1]
            : value;
    }
}
