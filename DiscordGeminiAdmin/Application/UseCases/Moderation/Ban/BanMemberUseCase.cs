using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.Ban;

public sealed record BanMember(Guid CaseId, BanSource Source);

public static class BanMemberUseCase
{
    public static async ValueTask<Result> Handle(
        BanMember command,
        IUnitOfWork unitOfWork,
        IModerationCaseRepository cases,
        IGuildProfileRepository guilds,
        IGuildGateway gateway,
        ModerationPolicy defaultPolicy,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var moderationCase = await cases.GetByIdAsync(command.CaseId, cancellationToken);
        if (moderationCase is null)
            return Result.Fail(ModerationErrors.CaseNotFound());

        if (command.Source == BanSource.Auto)
        {
            var guildProfile = await guilds.GetByGuildIdAsync(moderationCase.GuildId, cancellationToken);
            var policy = guildProfile?.ModerationPolicy ?? defaultPolicy;

            var profile = await gateway.GetMemberProfileAsync(
                moderationCase.GuildId, moderationCase.MemberId, cancellationToken);
            if (profile.IsFailed)
                return profile.ToResult();

            var autoBansLastHour = await cases.CountAutoBansSinceAsync(
                moderationCase.GuildId, now.AddHours(-1), cancellationToken);

            var verdict = policy.CanAutoBan(moderationCase.Assessment, profile.Value, autoBansLastHour);
            if (verdict.IsFailed)
                return verdict;
        }

        var banned = moderationCase.Ban(command.Source, now);
        if (banned.IsFailed)
            return banned;

        var discordResult = await gateway.BanAsync(
            moderationCase.GuildId, moderationCase.MemberId, moderationCase.Assessment.Reason.Value, cancellationToken);
        if (discordResult.IsFailed)
            return discordResult;

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
