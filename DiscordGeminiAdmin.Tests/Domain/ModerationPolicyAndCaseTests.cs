using DiscordGeminiAdmin.Application.EventHandlers;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Events;
using DiscordGeminiAdmin.SharedKernel;
using Shouldly;

namespace DiscordGeminiAdmin.Tests.Domain;

public class ModerationPolicyAndCaseTests
{
    private static readonly ModerationPolicy Live = ModerationPolicy.Default with { DryRun = false };
    private static readonly MemberProfile Regular = new(false, false, false, false);
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static RiskAssessment Assessment(double confidence = 0.99, int evidence = 5, string? rule = "rules#3") =>
        new(Confidence.From(confidence), ModerationReason.From("Спам ссылками"),
            rule is null ? null : RuleReference.From(rule), evidence);

    [Fact]
    public void Protected_member_is_never_auto_banned() =>
        Live.CanAutoBan(Assessment(), Regular with { IsStaff = true }, 0).ShouldHaveErrorCode("M101");

    [Fact]
    public void Missing_rule_reference_blocks_auto_ban() =>
        Live.CanAutoBan(Assessment(rule: null), Regular, 0).ShouldHaveErrorCode("M103");

    [Theory]
    [InlineData(0.94, 5)]
    [InlineData(0.99, 2)]
    public void Below_threshold_blocks_auto_ban(double confidence, int evidence) =>
        Live.CanAutoBan(Assessment(confidence, evidence), Regular, 0).ShouldHaveErrorCode("M102");

    [Fact]
    public void Hourly_limit_blocks_auto_ban() =>
        Live.CanAutoBan(Assessment(), Regular, Live.MaxAutoBansPerHour).ShouldHaveErrorCode("M104");

    [Fact]
    public void Dry_run_blocks_auto_ban() =>
        (Live with { DryRun = true }).CanAutoBan(Assessment(), Regular, 0).ShouldHaveErrorCode("M105");

    [Fact]
    public void Exact_threshold_passes() =>
        Live.CanAutoBan(Assessment(0.95, 3), Regular, 0).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Ban_and_revert_lifecycle_emits_domain_events_and_creates_precedents()
    {
        var moderationCase = ModerationCase.Open(GuildId.From(10), MemberId.From(20), Assessment(), Now);

        moderationCase.Ban(BanSource.Auto, Now).IsSuccess.ShouldBeTrue();
        moderationCase.Status.ShouldBe(ModerationCaseStatus.Banned);
        var banEvent = moderationCase.DomainEvents.OfType<MemberBannedDomainEvent>().ShouldHaveSingleItem();

        var banPrecedent = RecordModerationPrecedentDomainEventHandler.Handle(banEvent);
        banPrecedent.RuleCode.ShouldStartWith("precedent:ban:");

        // Повторный бан закрытого кейса запрещён (M202).
        moderationCase.Ban(BanSource.Moderator, Now).ShouldHaveErrorCode("M202");

        moderationCase.Revert(Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        moderationCase.Status.ShouldBe(ModerationCaseStatus.Reverted);
        var revertEvent = moderationCase.DomainEvents.OfType<BanRevertedDomainEvent>().ShouldHaveSingleItem();

        var revertPrecedent = RecordModerationPrecedentDomainEventHandler.Handle(revertEvent);
        revertPrecedent.RuleCode.ShouldStartWith("precedent:revert:");
    }

    [Fact]
    public void Dismiss_kick_and_timeout_enforce_open_status()
    {
        var moderationCase = ModerationCase.Open(GuildId.From(10), MemberId.From(20), Assessment(), Now);

        moderationCase.Revert(Now).ShouldHaveErrorCode("M203");
        moderationCase.Dismiss(Now).IsSuccess.ShouldBeTrue();
        moderationCase.DomainEvents.OfType<ModerationCaseDismissedDomainEvent>().ShouldHaveSingleItem();

        moderationCase.Dismiss(Now).ShouldHaveErrorCode("M204");
        moderationCase.Kick(Now).ShouldHaveErrorCode("M205");
        moderationCase.Timeout(Now).ShouldHaveErrorCode("M206");
    }
}
