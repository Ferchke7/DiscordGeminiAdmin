using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Application.UseCases.Moderation.RunHourly;
using DiscordGeminiAdmin.Domain.Digests;
using DiscordGeminiAdmin.Domain.Digests.Repositories;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NSubstitute;
using Shouldly;

namespace DiscordGeminiAdmin.Tests.UseCases;

public class RunHourlyModerationUseCaseTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IGuildProfileRepository _guilds = Substitute.For<IGuildProfileRepository>();
    private readonly IBufferedMessageRepository _bufferedMessages = Substitute.For<IBufferedMessageRepository>();
    private readonly IModerationCaseRepository _cases = Substitute.For<IModerationCaseRepository>();
    private readonly IHourlyDigestRepository _digests = Substitute.For<IHourlyDigestRepository>();
    private readonly IKnowledgeIndex _knowledgeIndex = Substitute.For<IKnowledgeIndex>();
    private readonly IModerationAi _moderationAi = Substitute.For<IModerationAi>();
    private readonly IGuildGateway _gateway = Substitute.For<IGuildGateway>();
    private readonly IDiscordNotifier _notifier = Substitute.For<IDiscordNotifier>();

    private readonly GuildId _guildId = GuildId.From(100);
    private readonly ChannelId _modChannelId = ChannelId.From(200);
    private readonly MemberId _offenderId = MemberId.From(300);
    private readonly DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private void SetupConfiguredGuild(bool dryRun)
    {
        var policy = new ModerationPolicy(MinConfidence: 0.95, MinEvidenceMessages: 3, MaxAutoBansPerHour: 3, DryRun: dryRun);
        var profile = GuildProfile.Create(_guildId, ServerTheme.From("C# и .NET"), _modChannelId, policy, _now);
        _guilds.GetByGuildIdAsync(_guildId, Arg.Any<CancellationToken>()).Returns(profile);

        var msgs = Enumerable.Range(1, 3)
            .Select(i => BufferedMessage.Create(
                _guildId, _modChannelId, _offenderId, MessageId.From((ulong)i), "spammer", $"spam {i}", _now.AddMinutes(-10)))
            .ToList();
        _bufferedMessages.GetSinceAsync(_guildId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(msgs);

        _knowledgeIndex.GetRulesAndPrecedentsAsync(_guildId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new KnowledgeHitDto("rules#1", KnowledgeSourceType.Rules, "Анти-спам", "Запрещён спам")]);

        _gateway.GetMemberProfileAsync(_guildId, _offenderId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new MemberProfile(false, false, false, false)));
    }

    private ValueTask<Result<RunHourlyModerationSummary>> Act() =>
        RunHourlyModerationUseCase.Handle(
            new RunHourlyModeration(_guildId),
            _unitOfWork, _guilds, _bufferedMessages, _cases, _digests,
            _knowledgeIndex, _moderationAi, _gateway, _notifier, _now, CancellationToken.None);

    [Fact]
    public async Task Unconfigured_guild_returns_G201()
    {
        _guilds.GetByGuildIdAsync(_guildId, Arg.Any<CancellationToken>()).Returns((GuildProfile?)null);

        var result = await Act();

        result.ShouldHaveErrorCode("G201");
    }

    [Fact]
    public async Task Hallucinated_rule_code_blocks_auto_ban_and_routes_to_digest()
    {
        SetupConfiguredGuild(dryRun: false);

        // ИИ вернул несуществующее правило "fake#999" -> пост-валидация сбрасывает его в null -> авто-бан блокируется (M103).
        _moderationAi.AssessBatchAsync(Arg.Any<GuildModerationContext>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<MemberAssessmentDto>>([
                new MemberAssessmentDto(
                    _offenderId, 0.99, "Спам", "fake#999",
                    [MessageId.From(1), MessageId.From(2), MessageId.From(3)])
            ]));

        var result = await Act();

        result.IsSuccess.ShouldBeTrue();
        result.Value.CasesOpened.ShouldBe(1);
        result.Value.AutoBannedCount.ShouldBe(0);
        result.Value.DigestId.ShouldNotBeNull();
        await _gateway.DidNotReceive().BanAsync(_guildId, _offenderId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).SendHourlyDigestAsync(_modChannelId, Arg.Any<HourlyDigest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dry_run_marks_item_in_digest_without_banning_in_discord()
    {
        SetupConfiguredGuild(dryRun: true);

        _moderationAi.AssessBatchAsync(Arg.Any<GuildModerationContext>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<MemberAssessmentDto>>([
                new MemberAssessmentDto(
                    _offenderId, 0.99, "Фишинг", "rules#1",
                    [MessageId.From(1), MessageId.From(2), MessageId.From(3)])
            ]));

        HourlyDigest? capturedDigest = null;
        _digests.When(d => d.Add(Arg.Any<HourlyDigest>())).Do(ci => capturedDigest = ci.Arg<HourlyDigest>());

        var result = await Act();

        result.IsSuccess.ShouldBeTrue();
        result.Value.AutoBannedCount.ShouldBe(0);
        capturedDigest.ShouldNotBeNull();
        capturedDigest.Items.ShouldHaveSingleItem().WasDryRunAutoBan.ShouldBeTrue();
        await _gateway.DidNotReceive().BanAsync(_guildId, _offenderId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Live_high_confidence_with_verified_rule_and_evidence_auto_bans_and_sends_audit_card()
    {
        SetupConfiguredGuild(dryRun: false);
        _gateway.BanAsync(_guildId, _offenderId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        _moderationAi.AssessBatchAsync(Arg.Any<GuildModerationContext>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<MemberAssessmentDto>>([
                new MemberAssessmentDto(
                    _offenderId, 0.99, "Фишинг", "rules#1",
                    [MessageId.From(1), MessageId.From(2), MessageId.From(3)])
            ]));

        var result = await Act();

        result.IsSuccess.ShouldBeTrue();
        result.Value.CasesOpened.ShouldBe(1);
        result.Value.AutoBannedCount.ShouldBe(1);
        result.Value.DigestId.ShouldBeNull();
        await _gateway.Received(1).BanAsync(_guildId, _offenderId, "Фишинг", Arg.Any<CancellationToken>());
        await _notifier.Received(1).SendAutoBanAuditCardAsync(_modChannelId, Arg.Any<ModerationCase>(), Arg.Any<CancellationToken>());
    }
}
