using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Domain.Structure.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Structure.Analyze;

public sealed record AnalyzeGuildStructure(GuildId GuildId, string? Goal, ChannelId? FallbackChannelId = null);

public static class AnalyzeGuildStructureUseCase
{
    public static async ValueTask<Result<StructureProposal>> Handle(
        AnalyzeGuildStructure command,
        IUnitOfWork unitOfWork,
        IGuildProfileRepository guilds,
        IStructureProposalRepository proposals,
        IGuildGateway gateway,
        IKnowledgeIndex knowledgeIndex,
        IStructureAdvisorAi advisorAi,
        IDiscordNotifier notifier,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var profile = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        if (profile is null && command.FallbackChannelId is null)
            return Result.Fail(GuildErrors.NotConfigured());

        var theme = profile?.Theme.Value ?? command.Goal ?? "Современное IT и Discord-сообщество";
        var targetChannelId = profile?.ModChannelId ?? command.FallbackChannelId!.Value;

        var structureSnapshot = await gateway.GetGuildStructureAsync(command.GuildId, cancellationToken);
        if (structureSnapshot.IsFailed)
            return structureSnapshot.ToResult();

        var knowledge = await knowledgeIndex.GetRulesAndPrecedentsAsync(command.GuildId, 20, cancellationToken);

        var adviceResult = await advisorAi.ProposeStructureAsync(
            new GuildStructureContext(
                command.GuildId,
                theme,
                command.Goal,
                structureSnapshot.Value,
                knowledge),
            cancellationToken);
        if (adviceResult.IsFailed)
            return adviceResult.ToResult();

        var proposalResult = StructureProposal.Create(
            command.GuildId,
            adviceResult.Value.Summary,
            adviceResult.Value.Changes,
            now);
        if (proposalResult.IsFailed)
            return proposalResult;

        var proposal = proposalResult.Value;
        proposals.Add(proposal);
        await unitOfWork.SaveAsync(cancellationToken);

        await notifier.SendStructureProposalAsync(targetChannelId, proposal, cancellationToken);
        return Result.Ok(proposal);
    }
}
