namespace Agon.Application.Services;

public static class StreamingChunker
{
    public static IReadOnlyList<string> BuildSegments(
        string message,
        int targetChunkSize = 120,
        int maxSegments = 40)
    {
        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return [string.Empty];
        }

        if (trimmed.Length <= targetChunkSize)
        {
            return [trimmed];
        }

        var segmentCount = (int)Math.Ceiling(trimmed.Length / (double)targetChunkSize);
        segmentCount = Math.Clamp(segmentCount, 2, maxSegments);

        var step = Math.Max(1, trimmed.Length / segmentCount);
        var segments = new List<string>(segmentCount);

        for (var end = step; end < trimmed.Length; end += step)
        {
            var adjustedEnd = AdjustToWordBoundary(trimmed, end);
            var segment = trimmed[..adjustedEnd].TrimEnd();
            if (segment.Length == 0 || (segments.Count > 0 && segment.Length <= segments[^1].Length))
            {
                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0 || !segments[^1].Equals(trimmed, StringComparison.Ordinal))
        {
            segments.Add(trimmed);
        }

        return segments;
    }

    private static int AdjustToWordBoundary(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return Math.Clamp(index, 0, text.Length);
        }

        const int window = 24;
        var start = Math.Max(0, index - window);
        var end = Math.Min(text.Length - 1, index + window);

        for (var i = index; i >= start; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        for (var i = index; i <= end; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return Math.Clamp(index, 0, text.Length);
    }
}
