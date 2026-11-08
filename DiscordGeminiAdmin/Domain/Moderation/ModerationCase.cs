using DiscordGeminiAdmin.Domain.Moderation.Events;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Moderation;

/// <summary>Кейс модерации участника Discord-сервера.</summary>
public sealed class ModerationCase(Guid id) : AggregationRoot<Guid>(id)
{
    public required GuildId GuildId { get; init; }
    public required MemberId MemberId { get; init; }
    public required RiskAssessment Assessment { get; init; }
    public required DateTime OpenedAt { get; init; }
    public ModerationCaseStatus Status { get; private set; } = ModerationCaseStatus.Open;
    public BanSource? BanSource { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public static ModerationCase Open(GuildId guildId, MemberId memberId, RiskAssessment assessment, DateTime now) =>
        new(Guid.CreateVersion7())
        {
            GuildId = guildId,
            MemberId = memberId,
            Assessment = assessment,
            OpenedAt = now
        };

    public Result Ban(BanSource source, DateTime now)
    {
        if (Status != ModerationCaseStatus.Open)
            return Result.Fail(ModerationErrors.InvalidStatusForBan());

        Status = ModerationCaseStatus.Banned;
        BanSource = source;
        ResolvedAt = now;
        AddDomainEvent(new MemberBannedDomainEvent(
            Id, GuildId, MemberId, Assessment.Reason.Value, Assessment.Rule?.Value, source));
        return Result.Ok();
    }

    public Result Kick(DateTime now)
    {
        if (Status != ModerationCaseStatus.Open)
            return Result.Fail(ModerationErrors.InvalidStatusForKick());

        Status = ModerationCaseStatus.Kicked;
        ResolvedAt = now;
        return Result.Ok();
    }

    public Result Timeout(DateTime now)
    {
        if (Status != ModerationCaseStatus.Open)
            return Result.Fail(ModerationErrors.InvalidStatusForTimeout());

        Status = ModerationCaseStatus.TimedOut;
        ResolvedAt = now;
        return Result.Ok();
    }

    public Result Dismiss(DateTime now)
    {
        if (Status != ModerationCaseStatus.Open)
            return Result.Fail(ModerationErrors.InvalidStatusForDismiss());

        Status = ModerationCaseStatus.Dismissed;
        ResolvedAt = now;
        AddDomainEvent(new ModerationCaseDismissedDomainEvent(
            Id, GuildId, MemberId, Assessment.Reason.Value, Assessment.Rule?.Value));
        return Result.Ok();
    }

    public Result Revert(DateTime now)
    {
        if (Status != ModerationCaseStatus.Banned)
            return Result.Fail(ModerationErrors.InvalidStatusForRevert());

        Status = ModerationCaseStatus.Reverted;
        ResolvedAt = now;
        AddDomainEvent(new BanRevertedDomainEvent(
            Id, GuildId, MemberId, Assessment.Reason.Value, Assessment.Rule?.Value));
        return Result.Ok();
    }
}
