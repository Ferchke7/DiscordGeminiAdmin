using DiscordGeminiAdmin.SharedKernel;

namespace DiscordGeminiAdmin.Domain.Moderation.Repositories;

public interface IModerationCaseRepository
{
    void Add(ModerationCase moderationCase);
    Task<ModerationCase?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<int> CountAutoBansSinceAsync(GuildId guildId, DateTime since, CancellationToken ct);
    Task<IReadOnlyList<ModerationCase>> GetByMemberAsync(GuildId guildId, MemberId memberId, int limit, CancellationToken ct);
    Task<IReadOnlyList<ModerationCase>> GetRecentByGuildAsync(GuildId guildId, int limit, CancellationToken ct);
}

public interface IBufferedMessageRepository
{
    void Add(BufferedMessage message);
    Task<IReadOnlyList<BufferedMessage>> GetSinceAsync(GuildId guildId, DateTime since, CancellationToken ct);
    Task<IReadOnlyList<BufferedMessage>> GetByAuthorSinceAsync(GuildId guildId, MemberId authorId, DateTime since, CancellationToken ct);
    Task<IReadOnlyList<BufferedMessage>> GetByChannelSinceAsync(GuildId guildId, ChannelId channelId, DateTime since, int limit, CancellationToken ct);
    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct);
}
