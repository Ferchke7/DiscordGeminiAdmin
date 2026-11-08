using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.Dependencies.Knowledge;

public sealed record TextChunkDto(string SubRuleCode, string Title, string Content);

public sealed record KnowledgeChunkWriteDto(
    Guid DocumentId,
    GuildId GuildId,
    string RuleCode,
    KnowledgeSourceType SourceType,
    string Title,
    string Content,
    float[] Embedding);

public sealed record KnowledgeHitDto(
    string RuleCode,
    KnowledgeSourceType SourceType,
    string Title,
    string Content);

public interface ITextChunker
{
    IReadOnlyList<TextChunkDto> Chunk(string baseRuleCode, string title, string content);
}

public interface IEmbeddingService
{
    Task<Result<float[]>> EmbedDocumentAsync(string text, CancellationToken ct);
    Task<Result<float[]>> EmbedQueryAsync(string query, CancellationToken ct);
}

public interface IKnowledgeIndex
{
    Task ReplaceDocumentChunksAsync(
        Guid documentId,
        GuildId guildId,
        IReadOnlyList<KnowledgeChunkWriteDto> chunks,
        CancellationToken ct);

    Task<IReadOnlyList<KnowledgeHitDto>> SearchAsync(
        GuildId guildId,
        float[] queryVector,
        int topK,
        CancellationToken ct);

    Task<IReadOnlyList<KnowledgeHitDto>> GetRulesAndPrecedentsAsync(
        GuildId guildId,
        int maxCount,
        CancellationToken ct);
}
