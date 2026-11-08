using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Domain.Structure.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Structure.Analyze;

public sealed record AnalyzeGuildStructure(
    GuildId GuildId,
    string? Goal,
    ChannelId? FallbackChannelId = null,
    bool ApplyImmediately = false,
    string? NewTheme = null,
    bool IncludeChannelHistory = false);

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

        if (!string.IsNullOrWhiteSpace(command.NewTheme) && command.FallbackChannelId is { } fbChannel)
        {
            var trimmedTheme = command.NewTheme.Length > 500 ? command.NewTheme[..500] : command.NewTheme;
            if (ServerTheme.TryFrom(trimmedTheme, out var parsedTheme))
            {
                if (profile is null)
                {
                    profile = GuildProfile.Create(command.GuildId, parsedTheme, fbChannel, ModerationPolicy.Default, now);
                    guilds.Add(profile);
                }
                else
                {
                    profile.UpdateConfiguration(parsedTheme, profile.ModChannelId, profile.ModerationPolicy, now);
                }
            }
        }

        var theme = !string.IsNullOrWhiteSpace(command.NewTheme)
            ? command.NewTheme
            : profile?.Theme.Value ?? command.Goal ?? "Современное IT и Discord-сообщество";
        var targetChannelId = profile?.ModChannelId ?? command.FallbackChannelId!.Value;

        var structureSnapshot = await gateway.GetGuildStructureAsync(command.GuildId, cancellationToken);
        if (structureSnapshot.IsFailed)
            return structureSnapshot.ToResult();

        var knowledge = await knowledgeIndex.GetRulesAndPrecedentsAsync(command.GuildId, 20, cancellationToken);

        IReadOnlyList<string>? recentHistory = null;
        var wantsHistory = command.IncludeChannelHistory || ContainsHistoryKeywords(command.Goal);
        if (wantsHistory && command.FallbackChannelId is { } historyChannelId)
        {
            var historyRes = await gateway.GetRecentChannelHistoryAsync(historyChannelId, 35, cancellationToken);
            if (historyRes.IsSuccess)
                recentHistory = historyRes.Value;
        }

        var adviceResult = await advisorAi.ProposeStructureAsync(
            new GuildStructureContext(
                command.GuildId,
                theme,
                command.Goal,
                structureSnapshot.Value,
                knowledge,
                recentHistory),
            cancellationToken);
        if (adviceResult.IsFailed)
            return adviceResult.ToResult();

        // Если ИИ ответил на вопрос, написал код или нашёл информацию в истории чата (без изменения каналов),
        // возвращаем ответ в виде информационного объекта без создания пустого плана структуры в БД.
        if (command.ApplyImmediately && adviceResult.Value.Changes.Count == 0)
        {
            await unitOfWork.SaveAsync(cancellationToken);
            return Result.Ok(new StructureProposal(Guid.CreateVersion7())
            {
                GuildId = command.GuildId,
                Summary = adviceResult.Value.Summary.Trim(),
                Changes = [],
                CreatedAt = now
            });
        }

        var proposalResult = StructureProposal.Create(
            command.GuildId,
            adviceResult.Value.Summary,
            adviceResult.Value.Changes,
            now);
        if (proposalResult.IsFailed)
            return proposalResult;

        var proposal = proposalResult.Value;
        proposals.Add(proposal);

        if (command.ApplyImmediately)
        {
            proposal.Approve(now);
            var applyRes = await gateway.ApplyStructureChangesAsync(command.GuildId, proposal.Changes, cancellationToken);
            if (applyRes.IsFailed)
            {
                proposal.MarkFailed(now);
                await unitOfWork.SaveAsync(cancellationToken);
                return applyRes.ToResult<StructureProposal>();
            }

            proposal.MarkApplied(now);
            await unitOfWork.SaveAsync(cancellationToken);
            return Result.Ok(proposal);
        }

        await unitOfWork.SaveAsync(cancellationToken);
        await notifier.SendStructureProposalAsync(targetChannelId, proposal, cancellationToken);
        return Result.Ok(proposal);
    }

    private static bool ContainsHistoryKeywords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        return lower.Contains("истори")
               || lower.Contains("чат")
               || lower.Contains("переписк")
               || lower.Contains("писал")
               || lower.Contains("говорил")
               || lower.Contains("обсужд")
               || lower.Contains("саммари")
               || lower.Contains("сводк")
               || lower.Contains("выжимк")
               || lower.Contains("выше")
               || lower.Contains("ранее");
    }
}
