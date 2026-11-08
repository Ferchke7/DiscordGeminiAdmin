using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Domain.Structure.Repositories;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Structure.Approve;

public sealed record ApproveProposal(Guid ProposalId);

public static class ApproveProposalUseCase
{
    public static async ValueTask<Result> Handle(
        ApproveProposal command,
        IUnitOfWork unitOfWork,
        IStructureProposalRepository proposals,
        IGuildGateway gateway,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var proposal = await proposals.GetByIdAsync(command.ProposalId, cancellationToken);
        if (proposal is null)
            return Result.Fail(StructureErrors.ProposalNotFound());

        var approved = proposal.Approve(now);
        if (approved.IsFailed)
            return approved;

        var applyResult = await gateway.ApplyStructureChangesAsync(
            proposal.GuildId, proposal.Changes, cancellationToken);
        if (applyResult.IsFailed)
        {
            proposal.MarkFailed(now);
            await unitOfWork.SaveAsync(cancellationToken);
            return applyResult;
        }

        proposal.MarkApplied(now);
        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
