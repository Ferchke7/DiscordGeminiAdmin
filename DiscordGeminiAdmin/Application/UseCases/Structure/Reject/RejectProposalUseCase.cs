using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Domain.Structure.Repositories;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Structure.Reject;

public sealed record RejectProposal(Guid ProposalId);

public static class RejectProposalUseCase
{
    public static async ValueTask<Result> Handle(
        RejectProposal command,
        IUnitOfWork unitOfWork,
        IStructureProposalRepository proposals,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var proposal = await proposals.GetByIdAsync(command.ProposalId, cancellationToken);
        if (proposal is null)
            return Result.Fail(StructureErrors.ProposalNotFound());

        var rejected = proposal.Reject(now);
        if (rejected.IsFailed)
            return rejected;

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
