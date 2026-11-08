using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.RevertBan;

public sealed record RevertBan(Guid CaseId);

public static class RevertBanUseCase
{
    public static async ValueTask<Result> Handle(
        RevertBan command,
        IUnitOfWork unitOfWork,
        IModerationCaseRepository cases,
        IGuildGateway gateway,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var moderationCase = await cases.GetByIdAsync(command.CaseId, cancellationToken);
        if (moderationCase is null)
            return Result.Fail(ModerationErrors.CaseNotFound());

        var reverted = moderationCase.Revert(now);
        if (reverted.IsFailed)
            return reverted;

        var unbanResult = await gateway.UnbanAsync(
            moderationCase.GuildId,
            moderationCase.MemberId,
            $"Разбан по решению модератора (кейс {moderationCase.Id})",
            cancellationToken);
        if (unbanResult.IsFailed)
            return unbanResult;

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
