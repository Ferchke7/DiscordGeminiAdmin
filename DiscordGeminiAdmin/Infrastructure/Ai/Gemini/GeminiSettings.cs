namespace DiscordGeminiAdmin.Infrastructure.Ai.Gemini;

public sealed class GeminiSettings
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; init; } = string.Empty;
    public GeminiModelSettings Models { get; init; } = new();
    public int EmbeddingDimensions { get; init; } = 768;
}

public sealed class GeminiModelSettings
{
    public string Moderation { get; init; } = string.Empty;
    public string Structure { get; init; } = string.Empty;
    public string Embedding { get; init; } = string.Empty;
}
