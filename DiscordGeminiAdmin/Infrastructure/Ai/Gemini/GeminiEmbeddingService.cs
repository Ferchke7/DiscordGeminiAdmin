using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using FluentResults;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;

namespace DiscordGeminiAdmin.Infrastructure.Ai.Gemini;

internal sealed class GeminiEmbeddingService(
    Client client,
    IOptions<GeminiSettings> options,
    ILogger<GeminiEmbeddingService> logger) : IEmbeddingService
{
    public Task<Result<float[]>> EmbedDocumentAsync(string text, CancellationToken ct) =>
        EmbedCoreAsync(text, "RETRIEVAL_DOCUMENT", ct);

    public Task<Result<float[]>> EmbedQueryAsync(string query, CancellationToken ct) =>
        EmbedCoreAsync(query, "RETRIEVAL_QUERY", ct);

    private async Task<Result<float[]>> EmbedCoreAsync(string content, string taskType, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Models.Embedding))
            return Result.Fail("Не задана модель эмбеддингов Gemini:Models:Embedding");

        try
        {
            var response = await client.Models.EmbedContentAsync(
                model: settings.Models.Embedding,
                contents: content,
                config: new EmbedContentConfig
                {
                    OutputDimensionality = settings.EmbeddingDimensions,
                    TaskType = taskType
                });

            var rawValues = response.Embeddings?.FirstOrDefault()?.Values;
            if (rawValues is null || rawValues.Count == 0)
                return Result.Fail("Gemini вернул пустой вектор эмбеддинга");

            return Result.Ok(rawValues.Select(v => (float)v).ToArray());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка получения эмбеддинга от Gemini (taskType={TaskType})", taskType);
            return Result.Fail("Не удалось вычислить эмбеддинг через Gemini");
        }
    }
}
