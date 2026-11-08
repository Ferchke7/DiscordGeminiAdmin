using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.ScanMember;

public sealed record ScanMember(GuildId GuildId, MemberId MemberId);

public static class ScanMemberUseCase
{
    public static async ValueTask<Result<ModerationCase?>> Handle(
        ScanMember command,
        IUnitOfWork unitOfWork,
        IGuildProfileRepository guilds,
        IBufferedMessageRepository bufferedMessages,
        IModerationCaseRepository cases,
        IKnowledgeIndex knowledgeIndex,
        IModerationAi moderationAi,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var profile = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        if (profile is null)
            return Result.Fail(GuildErrors.NotConfigured());

        var messages = await bufferedMessages.GetByAuthorSinceAsync(
            command.GuildId, command.MemberId, now.AddHours(-24), cancellationToken);
        if (messages.Count == 0)
            return Result.Ok<ModerationCase?>(null);

        var ragRules = await knowledgeIndex.GetRulesAndPrecedentsAsync(command.GuildId, 30, cancellationToken);
        var validRuleCodes = ragRules.Select(r => r.RuleCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var batch = new MemberBatchInputDto(
            command.MemberId,
            messages[0].AuthorName,
            messages.Select(m => new MemberMessageSampleDto(m.DiscordMessageId, m.Content, m.CreatedAt)).ToList());

        var aiResult = await moderationAi.AssessBatchAsync(
            new GuildModerationContext(command.GuildId, profile.Theme.Value, ragRules, [batch]),
            cancellationToken);
        if (aiResult.IsFailed)
            return aiResult.ToResult();

        var item = aiResult.Value.FirstOrDefault(x => x.MemberId == command.MemberId);
        if (item is null
            || !Confidence.TryFrom(item.Confidence, out var confidence)
            || !ModerationReason.TryFrom(item.Reason, out var reason))
        {
            return Result.Ok<ModerationCase?>(null);
        }

        RuleReference? ruleRef = null;
        if (!string.IsNullOrWhiteSpace(item.RuleCode)
            && validRuleCodes.Contains(item.RuleCode)
            && RuleReference.TryFrom(item.RuleCode, out var validatedRule))
        {
            ruleRef = validatedRule;
        }

        var validIds = batch.Messages.Select(m => m.MessageId).ToHashSet();
        var evidenceCount = item.EvidenceMessageIds.Distinct().Count(validIds.Contains);

        var moderationCase = ModerationCase.Open(
            command.GuildId,
            command.MemberId,
            new RiskAssessment(confidence, reason, ruleRef, evidenceCount),
            now);

        cases.Add(moderationCase);
        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok<ModerationCase?>(moderationCase);
    }
}
