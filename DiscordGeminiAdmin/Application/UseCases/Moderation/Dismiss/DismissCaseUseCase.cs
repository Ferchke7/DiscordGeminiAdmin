using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.Dismiss;

public sealed record DismissCase(Guid CaseId);

public static class DismissCaseUseCase
{
    public static async ValueTask<Result> Handle(
        DismissCase command,
        IUnitOfWork unitOfWork,
        IModerationCaseRepository cases,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var moderationCase = await cases.GetByIdAsync(command.CaseId, cancellationToken);
        if (moderationCase is null)
            return Result.Fail(ModerationErrors.CaseNotFound());

        var dismissed = moderationCase.Dismiss(now);
        if (dismissed.IsFailed)
            return dismissed;

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
