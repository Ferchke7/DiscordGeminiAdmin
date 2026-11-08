using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Digests;
using DiscordGeminiAdmin.Domain.Digests.Repositories;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.RunHourly;

public sealed record RunHourlyModeration(GuildId GuildId);

public sealed record RunHourlyModerationSummary(
    int CasesOpened,
    int AutoBannedCount,
    Guid? DigestId);

public static class RunHourlyModerationUseCase
{
    private const double MinDigestConfidence = 0.50;
    private const int MaxAuthorsPerBatch = 20;
    private const int MaxMessagesPerAuthor = 30;
    private const int MaxRagRules = 30;

    public static async ValueTask<Result<RunHourlyModerationSummary>> Handle(
        RunHourlyModeration command,
        IUnitOfWork unitOfWork,
        IGuildProfileRepository guilds,
        IBufferedMessageRepository bufferedMessages,
        IModerationCaseRepository cases,
        IHourlyDigestRepository digests,
        IKnowledgeIndex knowledgeIndex,
        IModerationAi moderationAi,
        IGuildGateway gateway,
        IDiscordNotifier notifier,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var profile = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        if (profile is null)
            return Result.Fail(GuildErrors.NotConfigured());

        var periodStart = now.AddHours(-1);
        var recentMessages = await bufferedMessages.GetSinceAsync(command.GuildId, periodStart, cancellationToken);
        if (recentMessages.Count == 0)
            return Result.Ok(new RunHourlyModerationSummary(0, 0, null));

        var memberBatches = recentMessages
            .GroupBy(m => m.AuthorId)
            .OrderByDescending(g => g.Count())
            .Take(MaxAuthorsPerBatch)
            .Select(g => new MemberBatchInputDto(
                g.Key,
                g.First().AuthorName,
                g.OrderBy(m => m.CreatedAt)
                    .Take(MaxMessagesPerAuthor)
                    .Select(m => new MemberMessageSampleDto(
                        m.DiscordMessageId,
                        m.Content.Length > 500 ? m.Content[..500] : m.Content,
                        m.CreatedAt))
                    .ToList()))
            .ToList();

        var ragRules = await knowledgeIndex.GetRulesAndPrecedentsAsync(
            command.GuildId, MaxRagRules, cancellationToken);

        var validRuleCodes = ragRules
            .Select(r => r.RuleCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var aiResult = await moderationAi.AssessBatchAsync(
            new GuildModerationContext(command.GuildId, profile.Theme.Value, ragRules, memberBatches),
            cancellationToken);
        if (aiResult.IsFailed)
            return aiResult.ToResult();

        var messageIdsByMember = memberBatches.ToDictionary(
            b => b.MemberId,
            b => b.Messages.Select(m => m.MessageId).ToHashSet());

        var autoBansLastHour = await cases.CountAutoBansSinceAsync(command.GuildId, periodStart, cancellationToken);
        var digestItems = new List<HourlyDigestItem>();
        var openedCases = 0;
        var autoBanned = 0;

        foreach (var item in aiResult.Value.Where(x => x.Confidence >= MinDigestConfidence))
        {
            if (!Confidence.TryFrom(item.Confidence, out var confidence))
                continue;
            if (!ModerationReason.TryFrom(item.Reason, out var reason))
                continue;

            // Пост-валидация: принимаем только ссылку на правило, реально присутствующую в RAG этой гильдии.
            RuleReference? ruleRef = null;
            if (!string.IsNullOrWhiteSpace(item.RuleCode)
                && validRuleCodes.Contains(item.RuleCode)
                && RuleReference.TryFrom(item.RuleCode, out var validatedRule))
            {
                ruleRef = validatedRule;
            }

            // Пост-валидация доказательств: учитываем только ID сообщений из входного батча этого участника.
            var validMsgSet = messageIdsByMember.GetValueOrDefault(item.MemberId);
            var verifiedEvidenceCount = validMsgSet is null
                ? 0
                : item.EvidenceMessageIds.Distinct().Count(validMsgSet.Contains);

            var assessment = new RiskAssessment(confidence, reason, ruleRef, verifiedEvidenceCount);
            var moderationCase = ModerationCase.Open(command.GuildId, item.MemberId, assessment, now);
            cases.Add(moderationCase);
            openedCases++;

            var memberProfileResult = await gateway.GetMemberProfileAsync(
                command.GuildId, item.MemberId, cancellationToken);

            var policyVerdict = memberProfileResult.IsSuccess
                ? profile.ModerationPolicy.CanAutoBan(assessment, memberProfileResult.Value, autoBansLastHour)
                : memberProfileResult.ToResult();

            if (policyVerdict.IsSuccess)
            {
                var banDomain = moderationCase.Ban(BanSource.Auto, now);
                if (banDomain.IsSuccess)
                {
                    var banDiscord = await gateway.BanAsync(
                        command.GuildId, item.MemberId, reason.Value, cancellationToken);
                    if (banDiscord.IsSuccess)
                    {
                        autoBansLastHour++;
                        autoBanned++;
                        await notifier.SendAutoBanAuditCardAsync(profile.ModChannelId, moderationCase, cancellationToken);
                        continue;
                    }
                }
            }

            var isDryRunHit = policyVerdict.Errors.Any(
                e => (string?)e.Metadata.GetValueOrDefault("ErrorCode") == "M105");

            digestItems.Add(new HourlyDigestItem(
                moderationCase.Id,
                item.MemberId,
                confidence,
                reason.Value,
                ruleRef?.Value,
                WasDryRunAutoBan: isDryRunHit));
        }

        Guid? digestId = null;
        HourlyDigest? digestToPublish = null;

        if (digestItems.Count > 0)
        {
            var digestResult = HourlyDigest.Create(command.GuildId, periodStart, now, digestItems, now);
            if (digestResult.IsSuccess)
            {
                digestToPublish = digestResult.Value;
                digestId = digestToPublish.Id;
                digests.Add(digestToPublish);
            }
        }

        await unitOfWork.SaveAsync(cancellationToken);

        if (digestToPublish is not null)
        {
            await notifier.SendHourlyDigestAsync(profile.ModChannelId, digestToPublish, cancellationToken);
        }

        return Result.Ok(new RunHourlyModerationSummary(openedCases, autoBanned, digestId));
    }
}
