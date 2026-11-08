using DiscordGeminiAdmin.Domain.Digests;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.SharedKernel;
using Shouldly;

namespace DiscordGeminiAdmin.Tests.Domain;

public class GuildKnowledgeAndStructureDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Guild_profile_rejects_invalid_confidence_range()
    {
        var profile = GuildProfile.Create(
            GuildId.From(1),
            ServerTheme.From("C# и .NET"),
            ChannelId.From(100),
            ModerationPolicy.Default,
            Now);

        var badPolicy = ModerationPolicy.Default with { MinConfidence = 0.2 };
        profile.UpdateConfiguration(profile.Theme, profile.ModChannelId, badPolicy, Now)
            .ShouldHaveErrorCode("G101");
    }

    [Fact]
    public void Knowledge_document_detects_unchanged_content_via_sha256()
    {
        var created = KnowledgeDocument.Create(
            GuildId.From(1),
            RuleCode.From("rules#1"),
            KnowledgeSourceType.Rules,
            "Правило 1",
            "Запрещён спам\r\nссылками",
            Now);

        created.IsSuccess.ShouldBeTrue();
        var doc = created.Value;

        // Нормализация переводов строк (\r\n -> \n) даёт тот же SHA-256 -> переиндексация не нужна.
        var unchanged = doc.TryUpdateContent("Правило 1", "Запрещён спам\nссылками", Now);
        unchanged.IsSuccess.ShouldBeTrue();
        unchanged.Value.ShouldBeFalse();

        var changed = doc.TryUpdateContent("Правило 1", "Запрещён спам и фишинг", Now);
        changed.IsSuccess.ShouldBeTrue();
        changed.Value.ShouldBeTrue();
    }

    [Fact]
    public void Knowledge_document_validates_empty_and_oversized_content()
    {
        KnowledgeDocument.Create(GuildId.From(1), RuleCode.From("r1"), KnowledgeSourceType.Rules, "T", "  ", Now)
            .ShouldHaveErrorCode("K102");

        var huge = new string('a', KnowledgeErrors.MaxContentLength + 1);
        KnowledgeDocument.Create(GuildId.From(1), RuleCode.From("r1"), KnowledgeSourceType.Rules, "T", huge, Now)
            .ShouldHaveErrorCode("K101");
    }

    [Fact]
    public void Empty_digest_and_empty_structure_proposal_are_rejected()
    {
        HourlyDigest.Create(GuildId.From(1), Now.AddHours(-1), Now, [], Now)
            .ShouldHaveErrorCode("D101");

        StructureProposal.Create(GuildId.From(1), "План", [], Now)
            .ShouldHaveErrorCode("S101");
    }

    [Fact]
    public void Structure_proposal_cannot_be_approved_after_rejection()
    {
        var proposal = StructureProposal.Create(
            GuildId.From(1),
            "Добавить категорию .NET",
            [new ProposedChange(ProposedChangeKind.CreateCategory, ".NET", null, null, "Для тем по C#")],
            Now).Value;

        proposal.Reject(Now).IsSuccess.ShouldBeTrue();
        proposal.Approve(Now).ShouldHaveErrorCode("S202");
        proposal.Reject(Now).ShouldHaveErrorCode("S203");
    }
}
