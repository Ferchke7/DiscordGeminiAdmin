using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Knowledge.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.AdminActions;

public sealed record AdminBanMember(GuildId GuildId, MemberId TargetId, MemberId ModeratorId, string Reason);
public sealed record AdminUnbanMember(GuildId GuildId, MemberId TargetId, MemberId ModeratorId, string Reason);
public sealed record AdminKickMember(GuildId GuildId, MemberId TargetId, MemberId ModeratorId, string Reason);
public sealed record AdminTimeoutMember(GuildId GuildId, MemberId TargetId, MemberId ModeratorId, int Minutes, string Reason);
public sealed record AdminUntimeoutMember(GuildId GuildId, MemberId TargetId, MemberId ModeratorId, string Reason);
public sealed record AdminWarnMember(GuildId GuildId, MemberId TargetId, MemberId ModeratorId, string Reason, string? RuleCode);

public sealed record MemberHistoryReport(
    MemberProfile? Profile,
    IReadOnlyList<ModerationCase> Cases);

public sealed record GetMemberHistory(GuildId GuildId, MemberId TargetId);
public sealed record GetRecentGuildCases(GuildId GuildId, int Limit = 10);

public sealed record GuildDashboardReport(
    GuildProfile? Profile,
    GuildStructureSnapshot? Structure,
    int RulesCount,
    IReadOnlyList<ModerationCase> RecentCases);

public sealed record GetGuildDashboard(GuildId GuildId);

public sealed class AdminModerationUseCase(
    IGuildGateway guildGateway,
    IDiscordNotifier notifier,
    IGuildProfileRepository guildProfiles,
    IModerationCaseRepository cases,
    IKnowledgeDocumentRepository knowledgeDocuments,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<Guid>> Handle(AdminBanMember command, CancellationToken ct)
    {
        var profileRes = await guildGateway.GetMemberProfileAsync(command.GuildId, command.TargetId, ct);
        if (profileRes.IsSuccess && profileRes.Value.IsProtected)
            return Result.Fail(ModerationErrors.ProtectedMember);

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Ручной бан администратором" : command.Reason.Trim();
        var banRes = await guildGateway.BanAsync(command.GuildId, command.TargetId, reason, ct);
        if (banRes.IsFailed)
            return banRes.ToResult<Guid>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var assessment = new RiskAssessment(
            Confidence.From(1.0),
            ModerationReason.From(reason),
            null,
            1);

        var modCase = ModerationCase.Open(command.GuildId, command.TargetId, assessment, now);
        modCase.Ban(BanSource.Moderator, now);
        cases.Add(modCase);
        await unitOfWork.SaveAsync(ct);

        await NotifyIfConfiguredAsync(
            command.GuildId,
            "🔨 Ручной бан участника",
            command.TargetId,
            command.ModeratorId,
            reason,
            0xE74C3C,
            modCase.Id,
            ct);

        return Result.Ok(modCase.Id);
    }

    public async Task<Result> Handle(AdminUnbanMember command, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Разбан администратором" : command.Reason.Trim();
        var unbanRes = await guildGateway.UnbanAsync(command.GuildId, command.TargetId, reason, ct);
        if (unbanRes.IsFailed)
            return unbanRes;

        await NotifyIfConfiguredAsync(
            command.GuildId,
            "🔓 Разбан участника",
            command.TargetId,
            command.ModeratorId,
            reason,
            0x2ECC71,
            null,
            ct);

        return Result.Ok();
    }

    public async Task<Result<Guid>> Handle(AdminKickMember command, CancellationToken ct)
    {
        var profileRes = await guildGateway.GetMemberProfileAsync(command.GuildId, command.TargetId, ct);
        if (profileRes.IsSuccess && profileRes.Value.IsProtected)
            return Result.Fail(ModerationErrors.ProtectedMember);

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Ручной кик администратором" : command.Reason.Trim();
        var kickRes = await guildGateway.KickAsync(command.GuildId, command.TargetId, reason, ct);
        if (kickRes.IsFailed)
            return kickRes.ToResult<Guid>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var assessment = new RiskAssessment(
            Confidence.From(1.0),
            ModerationReason.From(reason),
            null,
            1);

        var modCase = ModerationCase.Open(command.GuildId, command.TargetId, assessment, now);
        modCase.Kick(now);
        cases.Add(modCase);
        await unitOfWork.SaveAsync(ct);

        await NotifyIfConfiguredAsync(
            command.GuildId,
            "👢 Кик участника с сервера",
            command.TargetId,
            command.ModeratorId,
            reason,
            0xE67E22,
            null,
            ct);

        return Result.Ok(modCase.Id);
    }

    public async Task<Result<Guid>> Handle(AdminTimeoutMember command, CancellationToken ct)
    {
        var profileRes = await guildGateway.GetMemberProfileAsync(command.GuildId, command.TargetId, ct);
        if (profileRes.IsSuccess && profileRes.Value.IsProtected)
            return Result.Fail(ModerationErrors.ProtectedMember);

        var minutes = Math.Clamp(command.Minutes, 1, 40320);
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? $"Таймаут на {minutes} мин." : command.Reason.Trim();
        var timeoutRes = await guildGateway.TimeoutAsync(
            command.GuildId,
            command.TargetId,
            TimeSpan.FromMinutes(minutes),
            reason,
            ct);

        if (timeoutRes.IsFailed)
            return timeoutRes.ToResult<Guid>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var assessment = new RiskAssessment(
            Confidence.From(1.0),
            ModerationReason.From($"[Таймаут {minutes} мин.] {reason}"),
            null,
            1);

        var modCase = ModerationCase.Open(command.GuildId, command.TargetId, assessment, now);
        modCase.Timeout(now);
        cases.Add(modCase);
        await unitOfWork.SaveAsync(ct);

        await NotifyIfConfiguredAsync(
            command.GuildId,
            $"🔇 Таймаут участника ({minutes} мин.)",
            command.TargetId,
            command.ModeratorId,
            reason,
            0xF39C12,
            null,
            ct);

        return Result.Ok(modCase.Id);
    }

    public async Task<Result> Handle(AdminUntimeoutMember command, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Снятие таймаута администратором" : command.Reason.Trim();
        var res = await guildGateway.RemoveTimeoutAsync(command.GuildId, command.TargetId, reason, ct);
        if (res.IsFailed)
            return res;

        await NotifyIfConfiguredAsync(
            command.GuildId,
            "🔊 Снятие таймаута",
            command.TargetId,
            command.ModeratorId,
            reason,
            0x2ECC71,
            null,
            ct);

        return Result.Ok();
    }

    public async Task<Result<(Guid CaseId, int TotalCases)>> Handle(AdminWarnMember command, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Предупреждение от модератора" : command.Reason.Trim();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ruleRef = !string.IsNullOrWhiteSpace(command.RuleCode) && RuleReference.TryFrom(command.RuleCode.Trim(), out var parsedRule)
            ? parsedRule
            : (RuleReference?)null;

        var assessment = new RiskAssessment(
            Confidence.From(1.0),
            ModerationReason.From($"[ПРЕДУПРЕЖДЕНИЕ] {reason}"),
            ruleRef,
            1);

        var modCase = ModerationCase.Open(command.GuildId, command.TargetId, assessment, now);
        cases.Add(modCase);
        await unitOfWork.SaveAsync(ct);

        var memberHistory = await cases.GetByMemberAsync(command.GuildId, command.TargetId, 50, ct);

        await NotifyIfConfiguredAsync(
            command.GuildId,
            $"⚠️ Предупреждение участнику (страйк #{memberHistory.Count})",
            command.TargetId,
            command.ModeratorId,
            reason,
            0xF1C40F,
            null,
            ct);

        return Result.Ok((modCase.Id, memberHistory.Count));
    }

    public async Task<Result<MemberHistoryReport>> Handle(GetMemberHistory query, CancellationToken ct)
    {
        var profileRes = await guildGateway.GetMemberProfileAsync(query.GuildId, query.TargetId, ct);
        var history = await cases.GetByMemberAsync(query.GuildId, query.TargetId, 15, ct);
        return Result.Ok(new MemberHistoryReport(
            profileRes.IsSuccess ? profileRes.Value : null,
            history));
    }

    public async Task<Result<IReadOnlyList<ModerationCase>>> Handle(GetRecentGuildCases query, CancellationToken ct)
    {
        var recent = await cases.GetRecentByGuildAsync(query.GuildId, Math.Clamp(query.Limit, 1, 25), ct);
        return Result.Ok(recent);
    }

    public async Task<Result<GuildDashboardReport>> Handle(GetGuildDashboard query, CancellationToken ct)
    {
        var profile = await guildProfiles.GetByGuildIdAsync(query.GuildId, ct);
        var structureRes = await guildGateway.GetGuildStructureAsync(query.GuildId, ct);
        var rules = await knowledgeDocuments.GetRuleCodesByGuildAsync(query.GuildId, ct);
        var recentCases = await cases.GetRecentByGuildAsync(query.GuildId, 8, ct);

        return Result.Ok(new GuildDashboardReport(
            profile,
            structureRes.IsSuccess ? structureRes.Value : null,
            rules.Count,
            recentCases));
    }

    private async Task NotifyIfConfiguredAsync(
        GuildId guildId,
        string actionTitle,
        MemberId? targetId,
        MemberId moderatorId,
        string reason,
        uint colorHex,
        Guid? unbanCaseId,
        CancellationToken ct)
    {
        var profile = await guildProfiles.GetByGuildIdAsync(guildId, ct);
        if (profile is not null)
        {
            await notifier.SendAdminActionLogAsync(
                profile.ModChannelId,
                actionTitle,
                targetId,
                moderatorId,
                reason,
                colorHex,
                unbanCaseId,
                ct);
        }
    }
}
