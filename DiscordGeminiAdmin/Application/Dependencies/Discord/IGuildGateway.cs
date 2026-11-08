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

/// <summary>Единственная точка побочных эффектов в Discord (бан, кик, таймаут, каналы, роли, структура).</summary>
public interface IGuildGateway
{
    Task<Result<MemberProfile>> GetMemberProfileAsync(GuildId guildId, MemberId memberId, CancellationToken ct);
    Task<Result> BanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
    Task<Result> UnbanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
    Task<Result> KickAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
    Task<Result> TimeoutAsync(GuildId guildId, MemberId memberId, TimeSpan duration, string reason, CancellationToken ct);
    Task<Result> RemoveTimeoutAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
    Task<Result<int>> PurgeMessagesAsync(ChannelId channelId, int count, MemberId? authorFilter, string reason, CancellationToken ct);
    Task<Result> SetSlowmodeAsync(ChannelId channelId, int seconds, string reason, CancellationToken ct);
    Task<Result> SetChannelLockAsync(GuildId guildId, ChannelId channelId, bool locked, string reason, CancellationToken ct);
    Task<Result> AssignRoleAsync(GuildId guildId, MemberId memberId, ulong roleId, string reason, CancellationToken ct);
    Task<Result> RemoveRoleAsync(GuildId guildId, MemberId memberId, ulong roleId, string reason, CancellationToken ct);
    Task<Result> SendAnnouncementAsync(ChannelId channelId, string title, string message, string style, ulong? mentionRoleId, CancellationToken ct);
    Task<Result<ChannelId>> CreateQuickChannelAsync(GuildId guildId, string name, string? categoryName, string? topic, bool isVoice, string reason, CancellationToken ct);
    Task<Result<GuildStructureSnapshot>> GetGuildStructureAsync(GuildId guildId, CancellationToken ct);
    Task<Result> ApplyStructureChangesAsync(GuildId guildId, IReadOnlyList<ProposedChange> changes, CancellationToken ct);
}
