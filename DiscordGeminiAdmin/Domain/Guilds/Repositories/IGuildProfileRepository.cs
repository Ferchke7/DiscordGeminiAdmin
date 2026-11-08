using DiscordGeminiAdmin.SharedKernel;

namespace DiscordGeminiAdmin.Domain.Guilds.Repositories;

public interface IGuildProfileRepository
{
    void Add(GuildProfile profile);
    Task<GuildProfile?> GetByGuildIdAsync(GuildId guildId, CancellationToken ct);
    Task<IReadOnlyList<GuildProfile>> GetAllAsync(CancellationToken ct);
}
