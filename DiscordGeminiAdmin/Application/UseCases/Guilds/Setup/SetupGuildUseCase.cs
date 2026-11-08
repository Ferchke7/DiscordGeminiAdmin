using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Guilds.Setup;

public sealed record SetupGuild(
    GuildId GuildId,
    string Theme,
    ChannelId ModChannelId,
    bool DryRun);

public sealed record SetGuildTheme(
    GuildId GuildId,
    string Theme,
    ChannelId FallbackModChannelId,
    bool? DryRun = null);

public sealed record SetGuildChannels(
    GuildId GuildId,
    ChannelId ModChannelId);

public static class SetupGuildUseCase
{
    public static async ValueTask<Result> Handle(
        SetupGuild command,
        IUnitOfWork unitOfWork,
        IGuildProfileRepository guilds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!ServerTheme.TryFrom(command.Theme, out var theme))
            return Result.Fail(GuildErrors.InvalidSettings("Тематика сервера обязательна и не длиннее 500 символов"));

        var existing = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        var policy = (existing?.ModerationPolicy ?? ModerationPolicy.Default) with { DryRun = command.DryRun };

        if (existing is null)
        {
            var profile = GuildProfile.Create(command.GuildId, theme, command.ModChannelId, policy, now);
            guilds.Add(profile);
        }
        else
        {
            var updated = existing.UpdateConfiguration(theme, command.ModChannelId, policy, now);
            if (updated.IsFailed)
                return updated;
        }

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }

    public static async ValueTask<Result<ChannelId>> Handle(
        SetGuildTheme command,
        IUnitOfWork unitOfWork,
        IGuildProfileRepository guilds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var trimmed = command.Theme.Length > 500 ? command.Theme[..500] : command.Theme;
        if (!ServerTheme.TryFrom(trimmed, out var theme))
            return Result.Fail(GuildErrors.InvalidSettings("Тематика сервера обязательна и не длиннее 500 символов"));

        var existing = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        var modChannelId = existing?.ModChannelId ?? command.FallbackModChannelId;
        var basePolicy = existing?.ModerationPolicy ?? ModerationPolicy.Default;
        var policy = command.DryRun is { } dr ? basePolicy with { DryRun = dr } : basePolicy;

        if (existing is null)
        {
            var profile = GuildProfile.Create(command.GuildId, theme, modChannelId, policy, now);
            guilds.Add(profile);
        }
        else
        {
            var updated = existing.UpdateConfiguration(theme, modChannelId, policy, now);
            if (updated.IsFailed)
                return updated.ToResult<ChannelId>();
        }

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok(modChannelId);
    }

    public static async ValueTask<Result<string>> Handle(
        SetGuildChannels command,
        IUnitOfWork unitOfWork,
        IGuildProfileRepository guilds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        var theme = existing?.Theme ?? ServerTheme.From("Discord-сообщество");
        var policy = existing?.ModerationPolicy ?? ModerationPolicy.Default;

        if (existing is null)
        {
            var profile = GuildProfile.Create(command.GuildId, theme, command.ModChannelId, policy, now);
            guilds.Add(profile);
        }
        else
        {
            var updated = existing.UpdateConfiguration(theme, command.ModChannelId, policy, now);
            if (updated.IsFailed)
                return updated.ToResult<string>();
        }

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok(theme.Value);
    }
}
