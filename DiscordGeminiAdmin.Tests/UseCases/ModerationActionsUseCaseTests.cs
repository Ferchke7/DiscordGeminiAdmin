using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Application.UseCases.Moderation.Ban;
using DiscordGeminiAdmin.Application.UseCases.Moderation.Dismiss;
using DiscordGeminiAdmin.Application.UseCases.Moderation.Kick;
using DiscordGeminiAdmin.Application.UseCases.Moderation.RevertBan;
using DiscordGeminiAdmin.Application.UseCases.Moderation.Timeout;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NSubstitute;
using Shouldly;

namespace DiscordGeminiAdmin.Tests.UseCases;

public class ModerationActionsUseCaseTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IModerationCaseRepository _cases = Substitute.For<IModerationCaseRepository>();
    private readonly IGuildProfileRepository _guilds = Substitute.For<IGuildProfileRepository>();
    private readonly IGuildGateway _gateway = Substitute.For<IGuildGateway>();
    private readonly ModerationPolicy _policy = ModerationPolicy.Default with { DryRun = false };
    private readonly DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private ModerationCase OpenCase(double confidence = 0.99, int evidence = 5) =>
        ModerationCase.Open(
            GuildId.From(1),
            MemberId.From(2),
            new RiskAssessment(Confidence.From(confidence), ModerationReason.From("Спам"), RuleReference.From("rules#3"), evidence),
            _now);

    [Fact]
    public async Task Unknown_case_is_rejected_with_M201()
    {
        _cases.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ModerationCase?)null);

        var result = await BanMemberUseCase.Handle(
            new BanMember(Guid.NewGuid(), BanSource.Moderator),
            _unitOfWork, _cases, _guilds, _gateway, _policy, _now, CancellationToken.None);

        result.ShouldHaveErrorCode("M201");
        await _unitOfWork.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Moderator_ban_skips_policy_and_saves()
    {
        var moderationCase = OpenCase(confidence: 0.1, evidence: 0);
        _cases.GetByIdAsync(moderationCase.Id, Arg.Any<CancellationToken>()).Returns(moderationCase);
        _gateway.BanAsync(moderationCase.GuildId, moderationCase.MemberId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        var result = await BanMemberUseCase.Handle(
            new BanMember(moderationCase.Id, BanSource.Moderator),
            _unitOfWork, _cases, _guilds, _gateway, _policy, _now, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        moderationCase.Status.ShouldBe(ModerationCaseStatus.Banned);
        await _unitOfWork.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Auto_ban_of_protected_member_is_rejected_and_does_not_call_discord()
    {
        var moderationCase = OpenCase();
        _cases.GetByIdAsync(moderationCase.Id, Arg.Any<CancellationToken>()).Returns(moderationCase);
        _gateway.GetMemberProfileAsync(moderationCase.GuildId, moderationCase.MemberId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new MemberProfile(IsOwner: true, IsBot: false, IsStaff: false, OutranksBot: false)));

        var result = await BanMemberUseCase.Handle(
            new BanMember(moderationCase.Id, BanSource.Auto),
            _unitOfWork, _cases, _guilds, _gateway, _policy, _now, CancellationToken.None);

        result.ShouldHaveErrorCode("M101");
        await _gateway.DidNotReceive().BanAsync(moderationCase.GuildId, moderationCase.MemberId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revert_ban_unbans_in_discord_and_saves()
    {
        var moderationCase = OpenCase();
        moderationCase.Ban(BanSource.Auto, _now);
        _cases.GetByIdAsync(moderationCase.Id, Arg.Any<CancellationToken>()).Returns(moderationCase);
        _gateway.UnbanAsync(moderationCase.GuildId, moderationCase.MemberId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        var result = await RevertBanUseCase.Handle(
            new RevertBan(moderationCase.Id),
            _unitOfWork, _cases, _gateway, _now.AddMinutes(10), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        moderationCase.Status.ShouldBe(ModerationCaseStatus.Reverted);
        await _unitOfWork.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Kick_timeout_and_dismiss_succeed_for_open_cases()
    {
        var case1 = OpenCase();
        _cases.GetByIdAsync(case1.Id, Arg.Any<CancellationToken>()).Returns(case1);
        _gateway.KickAsync(case1.GuildId, case1.MemberId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        (await KickMemberUseCase.Handle(new KickMember(case1.Id), _unitOfWork, _cases, _gateway, _now, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        case1.Status.ShouldBe(ModerationCaseStatus.Kicked);

        var case2 = OpenCase();
        _cases.GetByIdAsync(case2.Id, Arg.Any<CancellationToken>()).Returns(case2);
        _gateway.TimeoutAsync(case2.GuildId, case2.MemberId, TimeSpan.FromHours(1), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        (await TimeoutMemberUseCase.Handle(new TimeoutMember(case2.Id, TimeSpan.FromHours(1)), _unitOfWork, _cases, _gateway, _now, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        case2.Status.ShouldBe(ModerationCaseStatus.TimedOut);

        var case3 = OpenCase();
        _cases.GetByIdAsync(case3.Id, Arg.Any<CancellationToken>()).Returns(case3);

        (await DismissCaseUseCase.Handle(new DismissCase(case3.Id), _unitOfWork, _cases, _now, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        case3.Status.ShouldBe(ModerationCaseStatus.Dismissed);
    }
}
