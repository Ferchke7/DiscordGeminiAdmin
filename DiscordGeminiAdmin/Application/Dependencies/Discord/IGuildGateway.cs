using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.Dependencies.Discord;

public sealed record GuildChannelSnapshot(
    ChannelId Id,
    string Name,
    string? CategoryName,
    string? Topic,
    bool IsVoice);

public sealed record GuildStructureSnapshot(
    GuildId GuildId,
    string GuildName,
    IReadOnlyList<string> Categories,
    IReadOnlyList<GuildChannelSnapshot> Channels,
    IReadOnlyList<string> Roles);

/// <summary>Единственная точка побочных эффектов в Discord (бан, кик, таймаут, применение изменений структуры).</summary>
public interface IGuildGateway
{
    Task<Result<MemberProfile>> GetMemberProfileAsync(GuildId guildId, MemberId memberId, CancellationToken ct);
    Task<Result> BanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
    Task<Result> UnbanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
    Task<Result> KickAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
    Task<Result> TimeoutAsync(GuildId guildId, MemberId memberId, TimeSpan duration, string reason, CancellationToken ct);
    Task<Result<GuildStructureSnapshot>> GetGuildStructureAsync(GuildId guildId, CancellationToken ct);
    Task<Result> ApplyStructureChangesAsync(GuildId guildId, IReadOnlyList<ProposedChange> changes, CancellationToken ct);
}
