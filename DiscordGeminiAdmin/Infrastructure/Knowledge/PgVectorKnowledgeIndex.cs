using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Infrastructure.Persistence;
using DiscordGeminiAdmin.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DiscordGeminiAdmin.Infrastructure.Knowledge;

internal sealed class PgVectorKnowledgeIndex(DatabaseContext db, TimeProvider timeProvider) : IKnowledgeIndex
{
    public async Task ReplaceDocumentChunksAsync(
        Guid documentId,
        GuildId guildId,
        IReadOnlyList<KnowledgeChunkWriteDto> chunks,
        CancellationToken ct)
    {
        var existing = await db.KnowledgeChunks
            .Where(c => c.GuildId == guildId && c.DocumentId == documentId)
            .ToListAsync(ct);

        if (existing.Count > 0)
            db.KnowledgeChunks.RemoveRange(existing);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var dto in chunks)
        {
            db.KnowledgeChunks.Add(new KnowledgeChunkRecord
            {
                Id = Guid.CreateVersion7(),
                DocumentId = dto.DocumentId,
                GuildId = dto.GuildId,
                RuleCode = dto.RuleCode,
                SourceType = dto.SourceType,
                Title = dto.Title,
                Content = dto.Content,
                Embedding = new Vector(dto.Embedding),
                CreatedAt = now
            });
        }
    }

    public async Task<IReadOnlyList<KnowledgeHitDto>> SearchAsync(
        GuildId guildId,
        float[] queryVector,
        int topK,
        CancellationToken ct)
    {
        var query = new Vector(queryVector);

        // Строгая изоляция по GuildId перед векторной сортировкой CosineDistance.
        return await db.KnowledgeChunks
            .AsNoTracking()
            .Where(c => c.GuildId == guildId)
            .OrderBy(c => c.Embedding.CosineDistance(query))
            .Take(topK)
            .Select(c => new KnowledgeHitDto(c.RuleCode, c.SourceType, c.Title, c.Content))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeHitDto>> GetRulesAndPrecedentsAsync(
        GuildId guildId,
        int maxCount,
        CancellationToken ct)
    {
        return await db.KnowledgeChunks
            .AsNoTracking()
            .Where(c => c.GuildId == guildId)
            .OrderBy(c => c.SourceType)
            .ThenByDescending(c => c.CreatedAt)
            .Take(maxCount)
            .Select(c => new KnowledgeHitDto(c.RuleCode, c.SourceType, c.Title, c.Content))
            .ToListAsync(ct);
    }
}
