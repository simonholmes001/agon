using System.Text.Json;

namespace Agon.Infrastructure.Agents;

internal static class ProviderErrorSummary
{
    public static string FromResponseBody(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "No error details returned by provider.";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var fromError = TryExtractFromErrorElement(error);
                if (!string.IsNullOrWhiteSpace(fromError))
                {
                    return fromError!;
                }
            }

            if (TryGetString(root, "message", out var message))
            {
                return Sanitize(message!);
            }

            if (TryGetString(root, "detail", out var detail))
            {
                return Sanitize(detail!);
            }

            return "Provider returned no structured error message.";
        }
        catch (JsonException)
        {
            return "Provider returned a non-JSON error payload.";
        }
    }

    private static string? TryExtractFromErrorElement(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.String)
        {
            var errorText = error.GetString();
            return string.IsNullOrWhiteSpace(errorText)
                ? null
                : Sanitize(errorText);
        }

        if (error.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in error.EnumerateArray())
            {
                var nested = TryExtractFromErrorElement(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }

            return null;
        }

        if (error.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var hasMessage = TryGetString(error, "message", out var message)
            || TryGetString(error, "detail", out message);
        var hasType = TryGetString(error, "type", out var type)
            || TryGetString(error, "code", out type)
            || TryGetString(error, "reason", out type);

        if (hasType && hasMessage)
        {
            return Sanitize($"{type}: {message}");
        }

        if (hasMessage)
        {
            return Sanitize(message!);
        }

        if (hasType)
        {
            return Sanitize(type!);
        }

        return null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static string Sanitize(string value)
    {
        var collapsed = string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Trim();

        if (collapsed.Length == 0)
        {
            return "Provider returned an empty error message.";
        }

        return collapsed.Length <= 180
            ? collapsed
            : collapsed[..180];
    }
}
