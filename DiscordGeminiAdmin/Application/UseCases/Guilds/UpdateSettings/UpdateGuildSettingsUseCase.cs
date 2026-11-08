using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Guilds.UpdateSettings;

public sealed record UpdateGuildSettings(
    GuildId GuildId,
    double? MinConfidence,
    int? MinEvidenceMessages,
    int? MaxAutoBansPerHour,
    bool? DryRun);

public static class UpdateGuildSettingsUseCase
{
    public static async ValueTask<Result<ModerationPolicy>> Handle(
        UpdateGuildSettings command,
        IUnitOfWork unitOfWork,
        IGuildProfileRepository guilds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var profile = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        if (profile is null)
            return Result.Fail(GuildErrors.NotConfigured());

        var current = profile.ModerationPolicy;
        var nextPolicy = new ModerationPolicy(
            MinConfidence: command.MinConfidence ?? current.MinConfidence,
            MinEvidenceMessages: command.MinEvidenceMessages ?? current.MinEvidenceMessages,
            MaxAutoBansPerHour: command.MaxAutoBansPerHour ?? current.MaxAutoBansPerHour,
            DryRun: command.DryRun ?? current.DryRun);

        var updated = profile.UpdateConfiguration(profile.Theme, profile.ModChannelId, nextPolicy, now);
        if (updated.IsFailed)
            return Result.Fail<ModerationPolicy>(updated.Errors);

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok(nextPolicy);
    }
}
