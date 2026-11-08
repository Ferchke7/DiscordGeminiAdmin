using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;

namespace DiscordGeminiAdmin.Infrastructure.Discord;

internal sealed class NetCordGuildGateway(
    RestClient rest,
    TimeProvider timeProvider,
    ILogger<NetCordGuildGateway> logger) : IGuildGateway
{
    private const Permissions StaffPermissions =
        Permissions.Administrator | Permissions.ManageGuild | Permissions.BanUsers |
        Permissions.KickUsers | Permissions.ModerateUsers | Permissions.ManageMessages;

    public async Task<Result<MemberProfile>> GetMemberProfileAsync(
        GuildId guildId,
        MemberId memberId,
        CancellationToken ct)
    {
        try
        {
            var guild = await rest.GetGuildAsync(guildId.Value, cancellationToken: ct);
            var member = await rest.GetGuildUserAsync(guildId.Value, memberId.Value, cancellationToken: ct);
            var self = await rest.GetCurrentUserAsync(cancellationToken: ct);
            var bot = await rest.GetGuildUserAsync(guildId.Value, self.Id, cancellationToken: ct);

            var memberRoles = member.RoleIds.Select(id => guild.Roles.GetValueOrDefault(id)).OfType<Role>().ToList();
            var botRoles = bot.RoleIds.Select(id => guild.Roles.GetValueOrDefault(id)).OfType<Role>().ToList();

            var isStaff = memberRoles.Any(r => (r.Permissions & StaffPermissions) != 0);
            var memberTop = memberRoles.Select(r => r.Position).DefaultIfEmpty().Max();
            var botTop = botRoles.Select(r => r.Position).DefaultIfEmpty().Max();

            return Result.Ok(new MemberProfile(
                IsOwner: guild.OwnerId == memberId.Value,
                IsBot: member.IsBot,
                IsStaff: isStaff,
                OutranksBot: memberTop.CompareTo(botTop) >= 0));
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось получить профиль участника {MemberId}", memberId.Value);
            return Result.Fail("Не удалось получить участника из Discord");
        }
    }

    public async Task<Result> BanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct)
    {
        try
        {
            await rest.BanGuildUserAsync(
                guildId.Value,
                memberId.Value,
                0,
                new RestRequestProperties { AuditLogReason = reason },
                ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Discord отклонил бан {MemberId}", memberId.Value);
            return Result.Fail("Discord отклонил бан (проверьте права и иерархию ролей бота)");
        }
    }

    public async Task<Result> UnbanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct)
    {
        try
        {
            await rest.UnbanGuildUserAsync(
                guildId.Value,
                memberId.Value,
                new RestRequestProperties { AuditLogReason = reason },
                ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Discord отклонил разбан {MemberId}", memberId.Value);
            return Result.Fail("Discord отклонил разбан (возможно, пользователь не в бане)");
        }
    }

    public async Task<Result> KickAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct)
    {
        try
        {
            await rest.KickGuildUserAsync(
                guildId.Value,
                memberId.Value,
                new RestRequestProperties { AuditLogReason = reason },
                ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Discord отклонил кик {MemberId}", memberId.Value);
            return Result.Fail("Discord отклонил кик (проверьте иерархию ролей бота)");
        }
    }

    public async Task<Result> TimeoutAsync(
        GuildId guildId,
        MemberId memberId,
        TimeSpan duration,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var until = timeProvider.GetUtcNow().Add(duration);
            await rest.ModifyGuildUserAsync(
                guildId.Value,
                memberId.Value,
                opts => opts.TimeOutUntil = until,
                new RestRequestProperties { AuditLogReason = reason },
                ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Discord отклонил таймаут {MemberId}", memberId.Value);
            return Result.Fail("Discord отклонил таймаут (проверьте права Moderate Members)");
        }
    }

    public async Task<Result> RemoveTimeoutAsync(
        GuildId guildId,
        MemberId memberId,
        string reason,
        CancellationToken ct)
    {
        try
        {
            await rest.ModifyGuildUserAsync(
                guildId.Value,
                memberId.Value,
                opts => opts.TimeOutUntil = null,
                new RestRequestProperties { AuditLogReason = reason },
                ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Discord отклонил снятие таймаута {MemberId}", memberId.Value);
            return Result.Fail("Discord отклонил снятие таймаута");
        }
    }

    public async Task<Result<int>> PurgeMessagesAsync(
        ChannelId channelId,
        int count,
        MemberId? authorFilter,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var cutoff = timeProvider.GetUtcNow().AddDays(-14);
            var idsToDelete = new List<ulong>(count);

            await foreach (var msg in rest.GetMessagesAsync(channelId.Value).WithCancellation(ct))
            {
                if (msg.CreatedAt < cutoff)
                    break;

                if (authorFilter is not null && msg.Author.Id != authorFilter.Value.Value)
                    continue;

                idsToDelete.Add(msg.Id);
                if (idsToDelete.Count >= count)
                    break;
            }

            if (idsToDelete.Count == 0)
                return Result.Ok(0);

            var props = new RestRequestProperties { AuditLogReason = reason };
            if (idsToDelete.Count == 1)
            {
                await rest.DeleteMessageAsync(channelId.Value, idsToDelete[0], props, ct);
            }
            else
            {
                await rest.DeleteMessagesAsync(channelId.Value, idsToDelete, props, ct);
            }

            return Result.Ok(idsToDelete.Count);
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка очистки сообщений в канале {ChannelId}", channelId.Value);
            return Result.Fail("Discord отклонил удаление сообщений (проверьте право Manage Messages)");
        }
    }

    public async Task<Result> SetSlowmodeAsync(
        ChannelId channelId,
        int seconds,
        string reason,
        CancellationToken ct)
    {
        try
        {
            await rest.ModifyGuildChannelAsync(
                channelId.Value,
                opts => opts.Slowmode = seconds,
                new RestRequestProperties { AuditLogReason = reason },
                ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка установки slowmode в канале {ChannelId}", channelId.Value);
            return Result.Fail("Не удалось изменить медленный режим канала");
        }
    }

    public async Task<Result> SetChannelLockAsync(
        GuildId guildId,
        ChannelId channelId,
        bool locked,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var overwrite = new PermissionOverwriteProperties(guildId.Value, PermissionOverwriteType.Role)
            {
                Denied = locked ? Permissions.SendMessages : default
            };

            await rest.ModifyGuildChannelPermissionsAsync(
                channelId.Value,
                overwrite,
                new RestRequestProperties { AuditLogReason = reason },
                ct);

            var embed = new EmbedProperties
            {
                Title = locked ? "🔒 Канал временно закрыт" : "🔓 Канал снова открыт",
                Description = locked
                    ? $"Отправка сообщений временно приостановлена модератором.\n**Причина:** {reason}"
                    : $"Ограничение на отправку сообщений снято.\n**Комментарий:** {reason}",
                Color = new Color(locked ? 0xE74C3C : 0x2ECC71)
            };

            await rest.SendMessageAsync(
                channelId.Value,
                new MessageProperties { Embeds = [embed] },
                cancellationToken: ct);

            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка блокировки/разблокировки канала {ChannelId}", channelId.Value);
            return Result.Fail("Не удалось изменить права доступа канала");
        }
    }

    public async Task<Result> AssignRoleAsync(
        GuildId guildId,
        MemberId memberId,
        ulong roleId,
        string reason,
        CancellationToken ct)
    {
        try
        {
            await rest.AddGuildUserRoleAsync(
                guildId.Value,
                memberId.Value,
                roleId,
                new RestRequestProperties { AuditLogReason = reason },
                ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось выдать роль {RoleId} участнику {MemberId}", roleId, memberId.Value);
            return Result.Fail("Discord отклонил выдачу роли (роль бота должна быть выше выдаваемой роли)");
        }
    }

    public async Task<Result> RemoveRoleAsync(
        GuildId guildId,
        MemberId memberId,
        ulong roleId,
        string reason,
        CancellationToken ct)
    {
        try
        {
            await rest.RemoveGuildUserRoleAsync(
                guildId.Value,
                memberId.Value,
                roleId,
                new RestRequestProperties { AuditLogReason = reason },
                ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось снять роль {RoleId} у участника {MemberId}", roleId, memberId.Value);
            return Result.Fail("Discord отклонил снятие роли");
        }
    }

    public async Task<Result> SendAnnouncementAsync(
        ChannelId channelId,
        string title,
        string message,
        string style,
        ulong? mentionRoleId,
        CancellationToken ct)
    {
        try
        {
            var (color, icon) = style.ToLowerInvariant() switch
            {
                "warning" or "important" => (0xE74C3C, "🚨"),
                "event" => (0x9B59B6, "🎉"),
                "rules" => (0x2ECC71, "📜"),
                _ => (0x5865F2, "📢")
            };

            var embed = new EmbedProperties
            {
                Title = $"{icon} {title}",
                Description = message,
                Color = new Color(color),
                Timestamp = timeProvider.GetUtcNow(),
                Footer = new EmbedFooterProperties { Text = "Официальное объявление администрации сервера" }
            };

            var props = new MessageProperties
            {
                Content = mentionRoleId.HasValue ? $"<@&{mentionRoleId.Value}>" : null,
                Embeds = [embed]
            };

            await rest.SendMessageAsync(channelId.Value, props, cancellationToken: ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось отправить объявление в канал {ChannelId}", channelId.Value);
            return Result.Fail("Не удалось опубликовать объявление в указанный канал");
        }
    }

    public async Task<Result<ChannelId>> CreateQuickChannelAsync(
        GuildId guildId,
        string name,
        string? categoryName,
        string? topic,
        bool isVoice,
        string reason,
        CancellationToken ct)
    {
        try
        {
            ulong? parentId = null;
            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                var channels = await rest.GetGuildChannelsAsync(guildId.Value, cancellationToken: ct);
                var existingCat = channels
                    .OfType<CategoryGuildChannel>()
                    .FirstOrDefault(c => string.Equals(c.Name, categoryName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (existingCat is not null)
                {
                    parentId = existingCat.Id;
                }
                else
                {
                    var createdCat = await rest.CreateGuildChannelAsync(
                        guildId.Value,
                        new GuildChannelProperties(categoryName.Trim(), ChannelType.CategoryChannel),
                        new RestRequestProperties { AuditLogReason = reason },
                        ct);
                    parentId = createdCat.Id;
                }
            }

            var props = new GuildChannelProperties(
                name.Trim(),
                isVoice ? ChannelType.VoiceGuildChannel : ChannelType.TextGuildChannel)
            {
                ParentId = parentId,
                Topic = topic
            };

            var created = await rest.CreateGuildChannelAsync(
                guildId.Value,
                props,
                new RestRequestProperties { AuditLogReason = reason },
                ct);

            return Result.Ok(ChannelId.From(created.Id));
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка создания канала {ChannelName} в гильдии {GuildId}", name, guildId.Value);
            return Result.Fail("Discord отклонил создание канала (проверьте право Manage Channels)");
        }
    }

    public async Task<Result<GuildStructureSnapshot>> GetGuildStructureAsync(GuildId guildId, CancellationToken ct)
    {
        try
        {
            var guild = await rest.GetGuildAsync(guildId.Value, cancellationToken: ct);
            var channels = await rest.GetGuildChannelsAsync(guildId.Value, cancellationToken: ct);

            var categoryById = channels
                .OfType<CategoryGuildChannel>()
                .ToDictionary(c => c.Id, c => c.Name);

            var channelSnapshots = new List<GuildChannelSnapshot>();
            foreach (var ch in channels.Where(c => c is not CategoryGuildChannel))
            {
                if (!ChannelId.TryFrom(ch.Id, out var channelId))
                    continue;

                var parentId = (ch as TextGuildChannel)?.ParentId
                               ?? (ch as IVoiceGuildChannel)?.ParentId
                               ?? (ch as ForumGuildChannel)?.ParentId;
                var categoryName = parentId.HasValue ? categoryById.GetValueOrDefault(parentId.Value) : null;
                var topic = (ch as TextGuildChannel)?.Topic;
                var isVoice = ch is VoiceGuildChannel;

                channelSnapshots.Add(new GuildChannelSnapshot(
                    channelId,
                    ch.Name,
                    categoryName,
                    topic,
                    isVoice));
            }

            var roles = guild.Roles.Values
                .Where(r => r.Id != guild.Id)
                .OrderByDescending(r => r.Position)
                .Select(r => r.Name)
                .ToList();

            return Result.Ok(new GuildStructureSnapshot(
                guildId,
                guild.Name,
                categoryById.Values.OrderBy(x => x).ToList(),
                channelSnapshots,
                roles));
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось получить структуру гильдии {GuildId}", guildId.Value);
            return Result.Fail("Не удалось загрузить структуру сервера из Discord");
        }
    }

    public async Task<Result> ApplyStructureChangesAsync(
        GuildId guildId,
        IReadOnlyList<ProposedChange> changes,
        CancellationToken ct)
    {
        try
        {
            var existingChannels = (await rest.GetGuildChannelsAsync(guildId.Value, cancellationToken: ct)).ToList();
            var categoriesByName = existingChannels
                .OfType<CategoryGuildChannel>()
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            foreach (var step in changes)
            {
                var auditProps = new RestRequestProperties { AuditLogReason = step.Rationale };

                switch (step.Kind)
                {
                    case ProposedChangeKind.CreateCategory:
                    {
                        var created = await rest.CreateGuildChannelAsync(
                            guildId.Value,
                            new GuildChannelProperties(step.TargetName, ChannelType.CategoryChannel),
                            auditProps,
                            ct);
                        categoriesByName[step.TargetName] = created.Id;
                        break;
                    }
                    case ProposedChangeKind.CreateTextChannel:
                    case ProposedChangeKind.CreateVoiceChannel:
                    {
                        var type = step.Kind == ProposedChangeKind.CreateVoiceChannel
                            ? ChannelType.VoiceGuildChannel
                            : ChannelType.TextGuildChannel;
                        var props = new GuildChannelProperties(step.TargetName, type)
                        {
                            Topic = step.NewNameOrTopic
                        };
                        if (!string.IsNullOrWhiteSpace(step.ParentCategory)
                            && categoriesByName.TryGetValue(step.ParentCategory, out var parentId))
                        {
                            props.ParentId = parentId;
                        }
                        await rest.CreateGuildChannelAsync(guildId.Value, props, auditProps, ct);
                        break;
                    }
                    case ProposedChangeKind.UpdateChannelTopic:
                    {
                        var target = existingChannels.FirstOrDefault(
                            c => string.Equals(c.Name, step.TargetName, StringComparison.OrdinalIgnoreCase));
                        if (target is not null && !string.IsNullOrWhiteSpace(step.NewNameOrTopic))
                        {
                            await rest.ModifyGuildChannelAsync(
                                target.Id,
                                opts => opts.Topic = step.NewNameOrTopic,
                                auditProps,
                                ct);
                        }
                        break;
                    }
                    case ProposedChangeKind.RenameChannel:
                    {
                        var target = existingChannels.FirstOrDefault(
                            c => string.Equals(c.Name, step.TargetName, StringComparison.OrdinalIgnoreCase));
                        if (target is not null && !string.IsNullOrWhiteSpace(step.NewNameOrTopic))
                        {
                            await rest.ModifyGuildChannelAsync(
                                target.Id,
                                opts => opts.Name = step.NewNameOrTopic,
                                auditProps,
                                ct);
                        }
                        break;
                    }
                    case ProposedChangeKind.CreateRole:
                    {
                        await rest.CreateGuildRoleAsync(
                            guildId.Value,
                            new RoleProperties { Name = step.TargetName },
                            auditProps,
                            ct);
                        break;
                    }
                }
            }

            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка применения плана структуры для гильдии {GuildId}", guildId.Value);
            return Result.Fail("Discord отклонил одно из изменений структуры");
        }
    }
}
