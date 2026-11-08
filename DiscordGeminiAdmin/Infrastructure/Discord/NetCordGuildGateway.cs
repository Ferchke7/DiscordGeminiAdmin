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
            return Result.Fail("Discord отклонил бан");
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
            return Result.Fail("Discord отклонил разбан");
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
            return Result.Fail("Discord отклонил кик");
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
            return Result.Fail("Discord отклонил таймаут");
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
