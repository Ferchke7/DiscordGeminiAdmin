using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.Kick;

public sealed record KickMember(Guid CaseId);

public static class KickMemberUseCase
{
    public static async ValueTask<Result> Handle(
        KickMember command,
        IUnitOfWork unitOfWork,
        IModerationCaseRepository cases,
        IGuildGateway gateway,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var moderationCase = await cases.GetByIdAsync(command.CaseId, cancellationToken);
        if (moderationCase is null)
            return Result.Fail(ModerationErrors.CaseNotFound());

        var kicked = moderationCase.Kick(now);
        if (kicked.IsFailed)
            return kicked;

        var discordResult = await gateway.KickAsync(
            moderationCase.GuildId, moderationCase.MemberId, moderationCase.Assessment.Reason.Value, cancellationToken);
        if (discordResult.IsFailed)
            return discordResult;

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
