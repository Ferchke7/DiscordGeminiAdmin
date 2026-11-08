using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.ChannelActions;

public sealed record PurgeChannelMessages(
    GuildId GuildId,
    ChannelId ChannelId,
    MemberId ModeratorId,
    int Count,
    MemberId? AuthorFilter,
    string Reason);

public sealed record SetChannelSlowmode(
    GuildId GuildId,
    ChannelId ChannelId,
    MemberId ModeratorId,
    int Seconds,
    string Reason);

public sealed record LockUnlockChannel(
    GuildId GuildId,
    ChannelId ChannelId,
    MemberId ModeratorId,
    bool Locked,
    string Reason);

public sealed record PublishAnnouncement(
    GuildId GuildId,
    ChannelId ChannelId,
    MemberId ModeratorId,
    string Title,
    string Content,
    string Style,
    ulong? MentionRoleId);

public sealed record ManageMemberRole(
    GuildId GuildId,
    MemberId TargetId,
    MemberId ModeratorId,
    ulong RoleId,
    bool AddRole,
    string Reason);

public sealed record CreateServerChannel(
    GuildId GuildId,
    MemberId ModeratorId,
    string Name,
    string? CategoryName,
    string? Topic,
    bool IsVoice);

public sealed record SummarizeChannelMessages(
    GuildId GuildId,
    ChannelId ChannelId,
    string ChannelName,
    int Hours);

internal sealed class ChannelAdminUseCase(
    IGuildGateway guildGateway,
    IDiscordNotifier notifier,
    IGuildProfileRepository guildProfiles,
    IBufferedMessageRepository bufferedMessages,
    IModerationAi moderationAi,
    TimeProvider timeProvider)
{
    public async Task<Result<int>> Handle(PurgeChannelMessages command, CancellationToken ct)
    {
        var count = Math.Clamp(command.Count, 1, 100);
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? $"Очистка {count} сообщений" : command.Reason.Trim();
        var res = await guildGateway.PurgeMessagesAsync(command.ChannelId, count, command.AuthorFilter, reason, ct);
        if (res.IsFailed)
            return res;

        var profile = await guildProfiles.GetByGuildIdAsync(command.GuildId, ct);
        if (profile is not null)
        {
            await notifier.SendAdminActionLogAsync(
                profile.ModChannelId,
                $"🧹 Очистка чата (<#{command.ChannelId.Value}> — удалено {res.Value} сообщ.)",
                command.AuthorFilter,
                command.ModeratorId,
                reason,
                0x3498DB,
                null,
                ct);
        }

        return res;
    }

    public async Task<Result> Handle(SetChannelSlowmode command, CancellationToken ct)
    {
        var seconds = Math.Clamp(command.Seconds, 0, 21600);
        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? (seconds == 0 ? "Выключение slowmode" : $"Установка slowmode {seconds} сек.")
            : command.Reason.Trim();

        var res = await guildGateway.SetSlowmodeAsync(command.ChannelId, seconds, reason, ct);
        if (res.IsFailed)
            return res;

        var profile = await guildProfiles.GetByGuildIdAsync(command.GuildId, ct);
        if (profile is not null)
        {
            await notifier.SendAdminActionLogAsync(
                profile.ModChannelId,
                seconds == 0
                    ? $"⚡ Медленный режим выключен в <#{command.ChannelId.Value}>"
                    : $"🐢 Медленный режим ({seconds} сек.) в <#{command.ChannelId.Value}>",
                null,
                command.ModeratorId,
                reason,
                0x9B59B6,
                null,
                ct);
        }

        return Result.Ok();
    }

    public async Task<Result> Handle(LockUnlockChannel command, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? (command.Locked ? "Экстренная блокировка канала" : "Разблокировка канала")
            : command.Reason.Trim();

        var res = await guildGateway.SetChannelLockAsync(command.GuildId, command.ChannelId, command.Locked, reason, ct);
        if (res.IsFailed)
            return res;

        var profile = await guildProfiles.GetByGuildIdAsync(command.GuildId, ct);
        if (profile is not null)
        {
            await notifier.SendAdminActionLogAsync(
                profile.ModChannelId,
                command.Locked
                    ? $"🔒 Канал заблокирован: <#{command.ChannelId.Value}>"
                    : $"🔓 Канал разблокирован: <#{command.ChannelId.Value}>",
                null,
                command.ModeratorId,
                reason,
                command.Locked ? 0xE74C3C : 0x2ECC71u,
                null,
                ct);
        }

        return Result.Ok();
    }

    public Task<Result> Handle(PublishAnnouncement command, CancellationToken ct) =>
        guildGateway.SendAnnouncementAsync(
            command.ChannelId,
            command.Title,
            command.Content,
            command.Style,
            command.MentionRoleId,
            ct);

    public async Task<Result> Handle(ManageMemberRole command, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? (command.AddRole ? "Выдача роли администратором" : "Снятие роли администратором")
            : command.Reason.Trim();

        var res = command.AddRole
            ? await guildGateway.AssignRoleAsync(command.GuildId, command.TargetId, command.RoleId, reason, ct)
            : await guildGateway.RemoveRoleAsync(command.GuildId, command.TargetId, command.RoleId, reason, ct);

        if (res.IsFailed)
            return res;

        var profile = await guildProfiles.GetByGuildIdAsync(command.GuildId, ct);
        if (profile is not null)
        {
            await notifier.SendAdminActionLogAsync(
                profile.ModChannelId,
                command.AddRole ? $"🎭 Выдана роль <@&{command.RoleId}>" : $"🎭 Снята роль <@&{command.RoleId}>",
                command.TargetId,
                command.ModeratorId,
                reason,
                0x5865F2,
                null,
                ct);
        }

        return Result.Ok();
    }

    public Task<Result<ChannelId>> Handle(CreateServerChannel command, CancellationToken ct) =>
        guildGateway.CreateQuickChannelAsync(
            command.GuildId,
            command.Name,
            command.CategoryName,
            command.Topic,
            command.IsVoice,
            "Быстрое создание канала через /organize create-channel",
            ct);

    public async Task<Result<string>> Handle(SummarizeChannelMessages query, CancellationToken ct)
    {
        var hours = Math.Clamp(query.Hours, 1, 48);
        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-hours);
        var messages = await bufferedMessages.GetByChannelSinceAsync(query.GuildId, query.ChannelId, since, 100, ct);
        if (messages.Count == 0)
            return Result.Ok("За выбранный период в этом канале ещё нет сохранённых сообщений в буфере.");

        var profile = await guildProfiles.GetByGuildIdAsync(query.GuildId, ct);
        var theme = profile?.Theme.Value ?? "Общее сообщество";
        var formatted = messages
            .Select(m => $"[{m.CreatedAt:HH:mm}] {m.AuthorName}: {m.Content}")
            .ToList();

        return await moderationAi.SummarizeChannelAsync(theme, query.ChannelName, formatted, ct);
    }
}
