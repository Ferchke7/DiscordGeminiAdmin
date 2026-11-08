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
}
