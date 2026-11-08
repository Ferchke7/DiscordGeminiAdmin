using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.SharedKernel;
using Pgvector;

namespace DiscordGeminiAdmin.Infrastructure.Knowledge;

/// <summary>Запись векторного индекса знаний в PostgreSQL + pgvector.</summary>
public sealed class KnowledgeChunkRecord
{
    public required Guid Id { get; init; }
    public required Guid DocumentId { get; init; }
    public required GuildId GuildId { get; init; }
    public required string RuleCode { get; init; }
    public required KnowledgeSourceType SourceType { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required Vector Embedding { get; init; }
    public required DateTime CreatedAt { get; init; }
}
