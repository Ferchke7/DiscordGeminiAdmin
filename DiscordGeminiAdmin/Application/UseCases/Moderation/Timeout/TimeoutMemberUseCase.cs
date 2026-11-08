using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.Timeout;

public sealed record TimeoutMember(Guid CaseId, TimeSpan Duration);

public static class TimeoutMemberUseCase
{
    public static async ValueTask<Result> Handle(
        TimeoutMember command,
        IUnitOfWork unitOfWork,
        IModerationCaseRepository cases,
        IGuildGateway gateway,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var moderationCase = await cases.GetByIdAsync(command.CaseId, cancellationToken);
        if (moderationCase is null)
            return Result.Fail(ModerationErrors.CaseNotFound());

        var timedOut = moderationCase.Timeout(now);
        if (timedOut.IsFailed)
            return timedOut;

        var discordResult = await gateway.TimeoutAsync(
            moderationCase.GuildId,
            moderationCase.MemberId,
            command.Duration,
            moderationCase.Assessment.Reason.Value,
            cancellationToken);
        if (discordResult.IsFailed)
            return discordResult;

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
