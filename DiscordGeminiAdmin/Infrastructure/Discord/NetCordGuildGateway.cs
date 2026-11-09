using DiscordGeminiAdmin.Application.Dependencies.Ai;
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
            return Result.Fail($"Не удалось получить участника из Discord: {ex.Message}");
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
            return Result.Fail($"Discord отклонил бан (проверьте права и иерархию ролей бота): {ex.Message}");
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
            return Result.Fail($"Discord отклонил разбан (возможно, пользователь не в бане): {ex.Message}");
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
            return Result.Fail($"Discord отклонил кик (проверьте иерархию ролей бота): {ex.Message}");
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
            return Result.Fail($"Discord отклонил таймаут (проверьте права Moderate Members): {ex.Message}");
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
            return Result.Fail($"Discord отклонил снятие таймаута: {ex.Message}");
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
            return Result.Fail($"Discord отклонил удаление сообщений (проверьте право Manage Messages): {ex.Message}");
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
            return Result.Fail($"Не удалось изменить медленный режим канала: {ex.Message}");
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
            return Result.Fail($"Не удалось изменить права доступа канала: {ex.Message}");
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
            return Result.Fail($"Discord отклонил выдачу роли (роль бота должна быть выше выдаваемой роли): {ex.Message}");
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
            return Result.Fail($"Discord отклонил снятие роли: {ex.Message}");
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
            return Result.Fail($"Не удалось опубликовать объявление в указанный канал: {ex.Message}");
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

            var topicText = isVoice ? null : FormatChannelTopic(topic, reason);
            var props = new GuildChannelProperties(
                name.Trim(),
                isVoice ? ChannelType.VoiceGuildChannel : ChannelType.TextGuildChannel)
            {
                ParentId = parentId,
                Topic = topicText
            };

            var created = await rest.CreateGuildChannelAsync(
                guildId.Value,
                props,
                new RestRequestProperties { AuditLogReason = reason },
                ct);

            if (!isVoice)
            {
                await SendChannelDescriptionCardAsync(
                    created.Id,
                    name.Trim(),
                    categoryName,
                    topic,
                    reason,
                    ct);
            }

            return Result.Ok(ChannelId.From(created.Id));
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка создания канала {ChannelName} в гильдии {GuildId}", name, guildId.Value);
            return Result.Fail($"Discord отклонил создание канала (проверьте право Manage Channels): {ex.Message}");
        }
    }

    public async Task<Result<ulong>> CreateOrAssignRoleAsync(
        GuildId guildId,
        string roleName,
        MemberId? assignToMember,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var guild = await rest.GetGuildAsync(guildId.Value, cancellationToken: ct);
            var existingRole = guild.Roles.Values.FirstOrDefault(
                r => string.Equals(r.Name, roleName.Trim(), StringComparison.OrdinalIgnoreCase));

            ulong roleId;
            if (existingRole is not null)
            {
                roleId = existingRole.Id;
            }
            else
            {
                var created = await rest.CreateGuildRoleAsync(
                    guildId.Value,
                    new RoleProperties { Name = roleName.Trim(), Mentionable = true },
                    new RestRequestProperties { AuditLogReason = reason },
                    ct);
                roleId = created.Id;
            }

            if (assignToMember is not null)
            {
                await rest.AddGuildUserRoleAsync(
                    guildId.Value,
                    assignToMember.Value.Value,
                    roleId,
                    new RestRequestProperties { AuditLogReason = reason },
                    ct);
            }

            return Result.Ok(roleId);
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка создания/выдачи роли {RoleName} в гильдии {GuildId}", roleName, guildId.Value);
            return Result.Fail($"Discord отклонил создание или выдачу роли (проверьте право Manage Roles): {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<string>>> GetRecentChannelHistoryAsync(
        ChannelId channelId,
        int limit,
        CancellationToken ct)
    {
        try
        {
            var clamped = Math.Clamp(limit, 1, 100);
            var messages = new List<string>(clamped);

            await foreach (var msg in rest.GetMessagesAsync(channelId.Value).WithCancellation(ct))
            {
                if (!string.IsNullOrWhiteSpace(msg.Content))
                {
                    messages.Add($"[{msg.CreatedAt:dd.MM HH:mm}] @{msg.Author.Username}: {msg.Content}");
                }
                else if (msg.Embeds.Count > 0 && !string.IsNullOrWhiteSpace(msg.Embeds[0].Title))
                {
                    var desc = msg.Embeds[0].Description;
                    if (desc is not null && desc.Length > 200)
                        desc = desc[..200] + "...";
                    messages.Add($"[{msg.CreatedAt:dd.MM HH:mm}] @{msg.Author.Username} [Карточка]: {msg.Embeds[0].Title} — {desc}");
                }

                if (messages.Count >= clamped)
                    break;
            }

            messages.Reverse();
            return Result.Ok<IReadOnlyList<string>>(messages);
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось получить историю сообщений канала {ChannelId}", channelId.Value);
            return Result.Fail($"Не удалось прочитать историю канала: {ex.Message}");
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
            return Result.Fail($"Не удалось загрузить структуру сервера из Discord: {ex.Message}");
        }
    }

    public async Task<Result<int>> ResetGuildStructureAsync(
        GuildId guildId,
        ChannelId? keepChannelId,
        CancellationToken ct)
    {
        try
        {
            var removedCount = 0;
            var auditProps = new RestRequestProperties { AuditLogReason = "Полный сброс и пересборка сервера (/reset-setup)" };
            var channels = (await rest.GetGuildChannelsAsync(guildId.Value, cancellationToken: ct)).ToList();

            foreach (var ch in channels.Where(c => c is not CategoryGuildChannel && c.Id != keepChannelId?.Value))
            {
                try
                {
                    await rest.DeleteChannelAsync(ch.Id, auditProps, ct);
                    removedCount++;
                }
                catch (RestException ex)
                {
                    logger.LogWarning(ex, "Пропущено удаление защищённого канала {ChannelId}", ch.Id);
                }
            }

            if (keepChannelId.HasValue)
            {
                try
                {
                    await rest.ModifyGuildChannelAsync(
                        keepChannelId.Value.Value,
                        opts => opts.ParentId = null,
                        auditProps,
                        ct);
                }
                catch (RestException)
                {
                    // Игнорируем, если канал не в категории
                }
            }

            foreach (var cat in channels.OfType<CategoryGuildChannel>())
            {
                try
                {
                    await rest.DeleteChannelAsync(cat.Id, auditProps, ct);
                    removedCount++;
                }
                catch (RestException ex)
                {
                    logger.LogWarning(ex, "Пропущено удаление категории {CategoryId}", cat.Id);
                }
            }

            var guild = await rest.GetGuildAsync(guildId.Value, cancellationToken: ct);
            var self = await rest.GetCurrentUserAsync(cancellationToken: ct);
            var botMember = await rest.GetGuildUserAsync(guildId.Value, self.Id, cancellationToken: ct);
            var botTopPosition = botMember.RoleIds
                .Select(id => guild.Roles.GetValueOrDefault(id))
                .OfType<Role>()
                .Select(r => r.Position)
                .DefaultIfEmpty()
                .Max();

            foreach (var role in guild.Roles.Values.Where(r =>
                         r.Id != guild.Id
                         && !r.Managed
                         && r.Position.CompareTo(botTopPosition) < 0
                         && (r.Permissions & Permissions.Administrator) == 0))
            {
                try
                {
                    await rest.DeleteGuildRoleAsync(guildId.Value, role.Id, auditProps, ct);
                    removedCount++;
                }
                catch (RestException ex)
                {
                    logger.LogWarning(ex, "Пропущено удаление роли {RoleId}", role.Id);
                }
            }

            return Result.Ok(removedCount);
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка сброса структуры гильдии {GuildId}", guildId.Value);
            return Result.Fail($"Не удалось очистить старую структуру сервера: {ex.Message}");
        }
    }

    public async Task<Result> ApplyStructureChangesAsync(
        GuildId guildId,
        IReadOnlyList<ProposedChange> changes,
        CancellationToken ct)
    {
        try
        {
            var guild = await rest.GetGuildAsync(guildId.Value, cancellationToken: ct);
            var existingChannels = (await rest.GetGuildChannelsAsync(guildId.Value, cancellationToken: ct)).ToList();
            var categoriesByName = existingChannels
                .OfType<CategoryGuildChannel>()
                .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            var existingRoleNames = new HashSet<string>(
                guild.Roles.Values.Select(r => r.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var step in changes)
            {
                var auditProps = new RestRequestProperties { AuditLogReason = step.Rationale };
                var targetName = step.TargetName.Trim();
                if (string.IsNullOrWhiteSpace(targetName))
                    continue;

                switch (step.Kind)
                {
                    case ProposedChangeKind.CreateCategory:
                    {
                        if (!categoriesByName.ContainsKey(targetName))
                        {
                            var created = await rest.CreateGuildChannelAsync(
                                guildId.Value,
                                new GuildChannelProperties(targetName, ChannelType.CategoryChannel),
                                auditProps,
                                ct);
                            categoriesByName[targetName] = created.Id;
                        }
                        break;
                    }
                    case ProposedChangeKind.CreateTextChannel:
                    case ProposedChangeKind.CreateVoiceChannel:
                    {
                        var isVoice = step.Kind == ProposedChangeKind.CreateVoiceChannel;
                        ulong? parentId = null;

                        if (!string.IsNullOrWhiteSpace(step.ParentCategory))
                        {
                            var catName = step.ParentCategory.Trim();
                            if (!categoriesByName.TryGetValue(catName, out var existingCatId))
                            {
                                var createdCat = await rest.CreateGuildChannelAsync(
                                    guildId.Value,
                                    new GuildChannelProperties(catName, ChannelType.CategoryChannel),
                                    auditProps,
                                    ct);
                                existingCatId = createdCat.Id;
                                categoriesByName[catName] = existingCatId;
                            }
                            parentId = existingCatId;
                        }

                        var normalizedTarget = NormalizeChannelName(targetName);
                        var existingMatch = existingChannels.FirstOrDefault(c =>
                            c is not CategoryGuildChannel
                            && (isVoice ? c is VoiceGuildChannel : c is TextGuildChannel)
                            && (string.Equals(c.Name.Trim(), targetName, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(NormalizeChannelName(c.Name), normalizedTarget, StringComparison.OrdinalIgnoreCase)));

                        var topicText = isVoice ? null : FormatChannelTopic(step.NewNameOrTopic, step.Rationale);

                        if (existingMatch is not null)
                        {
                            if (parentId.HasValue || (!isVoice && !string.IsNullOrWhiteSpace(topicText)))
                            {
                                await rest.ModifyGuildChannelAsync(
                                    existingMatch.Id,
                                    opts =>
                                    {
                                        if (parentId.HasValue)
                                            opts.ParentId = parentId.Value;
                                        if (!isVoice && !string.IsNullOrWhiteSpace(topicText))
                                            opts.Topic = topicText;
                                    },
                                    auditProps,
                                    ct);
                            }

                            if (!isVoice)
                            {
                                await SendChannelDescriptionCardAsync(
                                    existingMatch.Id,
                                    existingMatch.Name,
                                    step.ParentCategory,
                                    step.NewNameOrTopic,
                                    step.Rationale,
                                    ct);
                            }
                            break;
                        }

                        var type = isVoice
                            ? ChannelType.VoiceGuildChannel
                            : ChannelType.TextGuildChannel;
                        var props = new GuildChannelProperties(targetName, type)
                        {
                            ParentId = parentId,
                            Topic = topicText
                        };
                        var createdCh = await rest.CreateGuildChannelAsync(guildId.Value, props, auditProps, ct);
                        existingChannels.Add(createdCh);

                        if (!isVoice)
                        {
                            await SendChannelDescriptionCardAsync(
                                createdCh.Id,
                                createdCh.Name,
                                step.ParentCategory,
                                step.NewNameOrTopic,
                                step.Rationale,
                                ct);
                        }
                        break;
                    }
                    case ProposedChangeKind.UpdateChannelTopic:
                    {
                        var normalizedTarget = NormalizeChannelName(targetName);
                        var target = existingChannels.FirstOrDefault(c =>
                            c is TextGuildChannel
                            && (string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(NormalizeChannelName(c.Name), normalizedTarget, StringComparison.OrdinalIgnoreCase)));

                        if (target is not null)
                        {
                            var topicText = FormatChannelTopic(step.NewNameOrTopic, step.Rationale);
                            if (!string.IsNullOrWhiteSpace(topicText))
                            {
                                await rest.ModifyGuildChannelAsync(
                                    target.Id,
                                    opts => opts.Topic = topicText,
                                    auditProps,
                                    ct);
                            }

                            await SendChannelDescriptionCardAsync(
                                target.Id,
                                target.Name,
                                step.ParentCategory,
                                step.NewNameOrTopic,
                                step.Rationale,
                                ct);
                        }
                        break;
                    }
                    case ProposedChangeKind.RenameChannel:
                    {
                        var target = existingChannels.FirstOrDefault(
                            c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase));
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
                        if (!existingRoleNames.Contains(targetName))
                        {
                            await rest.CreateGuildRoleAsync(
                                guildId.Value,
                                new RoleProperties { Name = targetName, Mentionable = true },
                                auditProps,
                                ct);
                            existingRoleNames.Add(targetName);
                        }
                        break;
                    }
                }
            }

            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка применения плана структуры для гильдии {GuildId}", guildId.Value);
            return Result.Fail($"Discord отклонил одно из изменений структуры: {ex.Message}");
        }
    }

    private async Task SendChannelDescriptionCardAsync(
        ulong channelId,
        string channelName,
        string? categoryName,
        string? description,
        string? rationale,
        CancellationToken ct)
    {
        try
        {
            var lowerName = channelName.ToLowerInvariant();
            var (icon, color) = lowerName switch
            {
                _ when lowerName.Contains("правил") || lowerName.Contains("инфо") || lowerName.Contains("rules") || lowerName.Contains("info") => ("📜", 0x2ECC71),
                _ when lowerName.Contains("новост") || lowerName.Contains("анонс") || lowerName.Contains("news") => ("📢", 0x9B59B6),
                _ when lowerName.Contains("работ") || lowerName.Contains("ваканс") || lowerName.Contains("резюме") || lowerName.Contains("фриланс") => ("💼", 0xF1C40F),
                _ when lowerName.Contains("код") || lowerName.Contains("бэкенд") || lowerName.Contains("фронтенд") || lowerName.Contains("devops") || lowerName.Contains("разработ") || lowerName.Contains("проект") => ("💻", 0x5865F2),
                _ => ("📌", 0x5865F2)
            };

            var bodyText = !string.IsNullOrWhiteSpace(description)
                ? description.Trim()
                : !string.IsNullOrWhiteSpace(rationale)
                    ? rationale.Trim()
                    : $"Добро пожаловать в канал **#{channelName}**!";

            if (!string.IsNullOrWhiteSpace(rationale)
                && !string.IsNullOrWhiteSpace(description)
                && !string.Equals(description.Trim(), rationale.Trim(), StringComparison.OrdinalIgnoreCase)
                && !description.Contains(rationale.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                bodyText += $"\n\n> 💡 **Назначение канала:** {rationale.Trim()}";
            }

            if (bodyText.Length > 3800)
                bodyText = bodyText[..3800] + "...";

            var footerText = !string.IsNullOrWhiteSpace(categoryName)
                ? $"Раздел: {categoryName.Trim()} • Настроено Code-Guru AI"
                : "Настроено Code-Guru AI";

            var embed = new EmbedProperties
            {
                Title = $"{icon} Добро пожаловать в #{channelName}",
                Description = bodyText,
                Color = new Color(color),
                Timestamp = timeProvider.GetUtcNow(),
                Footer = new EmbedFooterProperties { Text = footerText }
            };

            await rest.SendMessageAsync(
                channelId,
                new MessageProperties { Embeds = [embed] },
                cancellationToken: ct);
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось отправить стартовое описание в канал {ChannelId}", channelId);
        }
    }

    public async Task<Result<ChannelId>> EnsureNewsChannelAsync(
        GuildId guildId,
        string? channelName,
        string? categoryName,
        CancellationToken ct)
    {
        try
        {
            var targetName = string.IsNullOrWhiteSpace(channelName) ? "новости" : NormalizeChannelName(channelName);
            var channels = await rest.GetGuildChannelsAsync(guildId.Value, cancellationToken: ct);

            var existing = channels.OfType<TextGuildChannel>().FirstOrDefault(c =>
                string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains("новост", StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains("news", StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains("дайджест", StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
                return Result.Ok(ChannelId.From(existing.Id));

            ulong? categoryId = null;
            var targetCatName = string.IsNullOrWhiteSpace(categoryName) ? "📰 ИНФО И НОВОСТИ" : categoryName.Trim();
            var existingCat = channels.OfType<CategoryGuildChannel>().FirstOrDefault(c =>
                string.Equals(c.Name, targetCatName, StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains("новост", StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains("news", StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains("инфо", StringComparison.OrdinalIgnoreCase));

            if (existingCat is not null)
            {
                categoryId = existingCat.Id;
            }
            else
            {
                var catProps = new GuildChannelProperties(targetCatName, ChannelType.CategoryChannel);
                var createdCat = await rest.CreateGuildChannelAsync(
                    guildId.Value,
                    catProps,
                    new RestRequestProperties { AuditLogReason = "Создание категории для новостей сервера" },
                    ct);
                categoryId = createdCat.Id;
            }

            var channelProps = new GuildChannelProperties(targetName, ChannelType.TextGuildChannel)
            {
                ParentId = categoryId,
                Topic = "📰 Свежие новости IT, технологий, разработки ПО и искусственного интеллекта"
            };

            var createdChannel = await rest.CreateGuildChannelAsync(
                guildId.Value,
                channelProps,
                new RestRequestProperties { AuditLogReason = "Создание канала для новостей и дайджестов" },
                ct);

            await SendChannelDescriptionCardAsync(
                createdChannel.Id,
                createdChannel.Name,
                targetCatName,
                "📰 Канал свежих новостей и технологических дайджестов",
                "Здесь бот и модераторы публикуют самые интересные события, апдейты и открытия в IT, разработке и технологиях. Под каждой новостью автоматически создаётся ветка для обсуждения!",
                ct);

            return Result.Ok(ChannelId.From(createdChannel.Id));
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка при подготовке новостного канала на сервере {GuildId}", guildId.Value);
            return Result.Fail($"Не удалось подготовить канал новостей: {ex.Message}");
        }
    }

    public async Task<Result<NewsPublicationResult>> PublishNewsDigestAsync(
        GuildId guildId,
        ChannelId channelId,
        NewsDigestDto digest,
        bool createThread,
        string? customThreadTitle,
        CancellationToken ct)
    {
        try
        {
            var embed = new EmbedProperties
            {
                Title = digest.Headline.Length > 250 ? digest.Headline[..247] + "..." : digest.Headline,
                Description = digest.Introduction,
                Color = new Color(0x3498DB),
                Timestamp = timeProvider.GetUtcNow(),
                Footer = new EmbedFooterProperties
                {
                    Text = $"💬 Обсуждение в ветке • Code-Guru AI News"
                }
            };

            var fields = new List<EmbedFieldProperties>();
            foreach (var item in digest.Items.Take(10))
            {
                var fieldTitle = item.Title.Length > 250 ? item.Title[..247] + "..." : item.Title;
                var sourcePart = !string.IsNullOrWhiteSpace(item.SourceUrl) ? $"\n🔗 [Читать источник]({item.SourceUrl})" : string.Empty;
                var val = $"{item.Summary}\n\n💡 **Значение:** {item.Significance}{sourcePart}";
                if (val.Length > 1024)
                    val = val[..1020] + "...";

                fields.Add(new EmbedFieldProperties
                {
                    Name = fieldTitle,
                    Value = val,
                    Inline = false
                });
            }
            embed.Fields = fields;

            var sent = await rest.SendMessageAsync(
                channelId.Value,
                new MessageProperties { Embeds = [embed] },
                cancellationToken: ct);

            ulong? threadId = null;
            if (createThread)
            {
                try
                {
                    var threadTitle = !string.IsNullOrWhiteSpace(customThreadTitle)
                        ? customThreadTitle.Trim()
                        : $"💬 Обсуждение: {digest.Headline}";

                    if (threadTitle.Length > 95)
                        threadTitle = threadTitle[..92] + "...";

                    var thread = await rest.CreateGuildThreadAsync(
                        channelId.Value,
                        sent.Id,
                        new GuildThreadFromMessageProperties(threadTitle),
                        cancellationToken: ct);

                    threadId = thread.Id;

                    var kickoff = new MessageProperties
                    {
                        Content = $"👋 **Ветка обсуждения новостей открыта!**\n\n> 💭 *{digest.DiscussionQuestion}*\n\nДелитесь вашими мнениями, вопросами и интересными мыслями прямо в этой ветке! 👇"
                    };
                    await rest.SendMessageAsync(thread.Id, kickoff, cancellationToken: ct);
                }
                catch (RestException threadEx)
                {
                    logger.LogWarning(threadEx, "Не удалось создать ветку обсуждения новостей в канале {ChannelId}", channelId.Value);
                }
            }

            return Result.Ok(new NewsPublicationResult(channelId, sent.Id, threadId));
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Ошибка публикации новостей в канал {ChannelId}", channelId.Value);
            return Result.Fail($"Ошибка отправки новостей в Discord: {ex.Message}");
        }
    }

    private static string? FormatChannelTopic(string? topic, string? fallbackRationale)
    {
        var raw = !string.IsNullOrWhiteSpace(topic) ? topic.Trim() : fallbackRationale?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Length > 950 ? raw[..950] + "..." : raw;
    }

    private static string NormalizeChannelName(string name) =>
        name.Trim().Replace(' ', '-').ToLowerInvariant();
}
