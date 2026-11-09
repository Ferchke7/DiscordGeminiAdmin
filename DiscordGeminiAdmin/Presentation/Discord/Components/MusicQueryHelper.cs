namespace DiscordGeminiAdmin.Presentation.Discord.Components;

public static class MusicQueryHelper
{
    public static string Clean(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var trimmed = query.Trim();
        if (trimmed.StartsWith("query:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["query:".Length..].Trim();

        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) || (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
        {
            if (trimmed.Length >= 2)
                trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    public static bool HasSearchPrefix(string query) =>
        query.StartsWith("ytsearch:", StringComparison.OrdinalIgnoreCase)
        || query.StartsWith("scsearch:", StringComparison.OrdinalIgnoreCase)
        || query.StartsWith("spsearch:", StringComparison.OrdinalIgnoreCase)
        || query.StartsWith("ymsearch:", StringComparison.OrdinalIgnoreCase);

    public static bool IsUrl(string query) =>
        query.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || query.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
