using DiscordGeminiAdmin.Domain.Digests;
using DiscordGeminiAdmin.Domain.Digests.Repositories;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Knowledge.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Domain.Structure.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DiscordGeminiAdmin.Infrastructure.Persistence.Repositories;

internal sealed class GuildProfileRepository(DatabaseContext db) : IGuildProfileRepository
{
    public void Add(GuildProfile profile) => db.GuildProfiles.Add(profile);

    public Task<GuildProfile?> GetByGuildIdAsync(GuildId guildId, CancellationToken ct) =>
        db.GuildProfiles.FirstOrDefaultAsync(x => x.GuildId == guildId, ct);

    public async Task<IReadOnlyList<GuildProfile>> GetAllAsync(CancellationToken ct) =>
        await db.GuildProfiles.AsNoTracking().ToListAsync(ct);
}

internal sealed class KnowledgeDocumentRepository(DatabaseContext db) : IKnowledgeDocumentRepository
{
    public void Add(KnowledgeDocument document) => db.KnowledgeDocuments.Add(document);

    public void Remove(KnowledgeDocument document) => db.KnowledgeDocuments.Remove(document);

    public Task<KnowledgeDocument?> GetByRuleCodeAsync(GuildId guildId, RuleCode ruleCode, CancellationToken ct) =>
        db.KnowledgeDocuments.FirstOrDefaultAsync(x => x.GuildId == guildId && x.RuleCode == ruleCode, ct);

    public async Task<IReadOnlySet<string>> GetRuleCodesByGuildAsync(GuildId guildId, CancellationToken ct)
    {
        var codes = await db.KnowledgeDocuments
            .AsNoTracking()
            .Where(x => x.GuildId == guildId)
            .Select(x => x.RuleCode)
            .ToListAsync(ct);

        return codes.Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> GetAllByGuildAsync(GuildId guildId, CancellationToken ct) =>
        await db.KnowledgeDocuments
            .AsNoTracking()
            .Where(x => x.GuildId == guildId)
            .OrderBy(x => x.SourceType)
            .ThenBy(x => x.Title)
            .ToListAsync(ct);
}

internal sealed class ModerationCaseRepository(DatabaseContext db) : IModerationCaseRepository
{
    public void Add(ModerationCase moderationCase) => db.ModerationCases.Add(moderationCase);

    public Task<ModerationCase?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ModerationCases.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<int> CountAutoBansSinceAsync(GuildId guildId, DateTime since, CancellationToken ct) =>
        db.ModerationCases.CountAsync(
            x => x.GuildId == guildId
                 && x.Status == ModerationCaseStatus.Banned
                 && x.BanSource == BanSource.Auto
                 && x.ResolvedAt >= since,
            ct);

    public async Task<IReadOnlyList<ModerationCase>> GetByMemberAsync(
        GuildId guildId,
        MemberId memberId,
        int limit,
        CancellationToken ct) =>
        await db.ModerationCases
            .AsNoTracking()
            .Where(x => x.GuildId == guildId && x.MemberId == memberId)
            .OrderByDescending(x => x.OpenedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ModerationCase>> GetRecentByGuildAsync(
        GuildId guildId,
        int limit,
        CancellationToken ct) =>
        await db.ModerationCases
            .AsNoTracking()
            .Where(x => x.GuildId == guildId)
            .OrderByDescending(x => x.OpenedAt)
            .Take(limit)
            .ToListAsync(ct);
}

internal sealed class BufferedMessageRepository(DatabaseContext db) : IBufferedMessageRepository
{
    public void Add(BufferedMessage message) => db.BufferedMessages.Add(message);

    public async Task<IReadOnlyList<BufferedMessage>> GetSinceAsync(GuildId guildId, DateTime since, CancellationToken ct) =>
        await db.BufferedMessages
            .AsNoTracking()
            .Where(x => x.GuildId == guildId && x.CreatedAt >= since)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BufferedMessage>> GetByAuthorSinceAsync(
        GuildId guildId,
        MemberId authorId,
        DateTime since,
        CancellationToken ct) =>
        await db.BufferedMessages
            .AsNoTracking()
            .Where(x => x.GuildId == guildId && x.AuthorId == authorId && x.CreatedAt >= since)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BufferedMessage>> GetByChannelSinceAsync(
        GuildId guildId,
        ChannelId channelId,
        DateTime since,
        int limit,
        CancellationToken ct) =>
        await db.BufferedMessages
            .AsNoTracking()
            .Where(x => x.GuildId == guildId && x.ChannelId == channelId && x.CreatedAt >= since)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct) =>
        db.BufferedMessages
            .Where(x => x.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
}

internal sealed class HourlyDigestRepository(DatabaseContext db) : IHourlyDigestRepository
{
    public void Add(HourlyDigest digest) => db.HourlyDigests.Add(digest);

    public Task<HourlyDigest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.HourlyDigests.FirstOrDefaultAsync(x => x.Id == id, ct);
}

internal sealed class StructureProposalRepository(DatabaseContext db) : IStructureProposalRepository
{
    public void Add(StructureProposal proposal) => db.StructureProposals.Add(proposal);

    public Task<StructureProposal?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.StructureProposals.FirstOrDefaultAsync(x => x.Id == id, ct);
}
