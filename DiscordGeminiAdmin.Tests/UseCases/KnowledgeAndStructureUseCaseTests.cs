using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.IndexDocument;
using DiscordGeminiAdmin.Application.UseCases.Structure.Approve;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Knowledge.Repositories;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Domain.Structure.Repositories;
using DiscordGeminiAdmin.Infrastructure.Ai.Gemini;
using DiscordGeminiAdmin.Infrastructure.Knowledge;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NSubstitute;
using Shouldly;

namespace DiscordGeminiAdmin.Tests.UseCases;

public class KnowledgeAndStructureUseCaseTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IKnowledgeDocumentRepository _documents = Substitute.For<IKnowledgeDocumentRepository>();
    private readonly IEmbeddingService _embeddings = Substitute.For<IEmbeddingService>();
    private readonly IKnowledgeIndex _knowledgeIndex = Substitute.For<IKnowledgeIndex>();
    private readonly IStructureProposalRepository _proposals = Substitute.For<IStructureProposalRepository>();
    private readonly IGuildGateway _gateway = Substitute.For<IGuildGateway>();
    private readonly DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Index_document_skips_embedding_when_content_hash_is_unchanged()
    {
        var guildId = GuildId.From(1);
        var existing = KnowledgeDocument.Create(
            guildId, RuleCode.From("rules#1"), KnowledgeSourceType.Rules, "Правила", "Не спамить", _now).Value;

        _documents.GetByRuleCodeAsync(guildId, RuleCode.From("rules#1"), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await IndexDocumentUseCase.Handle(
            new IndexDocument(guildId, "rules#1", KnowledgeSourceType.Rules, "Правила", "Не спамить"),
            _unitOfWork, _documents, new MarkdownTextChunker(), _embeddings, _knowledgeIndex, _now, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WasSkippedUnchanged.ShouldBeTrue();
        await _embeddings.DidNotReceive().EmbedDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_proposal_applies_changes_in_discord_and_marks_applied()
    {
        var proposal = StructureProposal.Create(
            GuildId.From(1),
            "Создать канал #welcome",
            [new ProposedChange(ProposedChangeKind.CreateTextChannel, "welcome", null, "Приветствие", "Нужен входной канал")],
            _now).Value;

        _proposals.GetByIdAsync(proposal.Id, Arg.Any<CancellationToken>()).Returns(proposal);
        _gateway.ApplyStructureChangesAsync(proposal.GuildId, Arg.Any<IReadOnlyList<ProposedChange>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        var result = await ApproveProposalUseCase.Handle(
            new ApproveProposal(proposal.Id),
            _unitOfWork, _proposals, _gateway, _now, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        proposal.Status.ShouldBe(ProposalStatus.Applied);
        await _unitOfWork.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Gemini_json_parsers_handle_valid_and_invalid_payloads()
    {
        GeminiModerationAi.ParseAssessmentsJson("not-json").IsFailed.ShouldBeTrue();

        var validModJson = """
            [
              {
                "memberId": "12345",
                "confidence": 0.97,
                "reason": "Спам ссылками",
                "ruleId": "rules#1",
                "evidenceMessageIds": ["10", "20", "30"]
              }
            ]
            """;

        var parsedMod = GeminiModerationAi.ParseAssessmentsJson(validModJson);
        parsedMod.IsSuccess.ShouldBeTrue();
        var item = parsedMod.Value.ShouldHaveSingleItem();
        item.MemberId.ShouldBe(MemberId.From(12345));
        item.EvidenceMessageIds.Count.ShouldBe(3);

        var validStructureJson = """
            {
              "summary": "Улучшить навигацию",
              "changes": [
                {
                  "kind": "CreateCategory",
                  "targetName": "Разработка",
                  "rationale": "Сгруппировать технические каналы"
                }
              ]
            }
            """;

        var parsedStructure = GeminiStructureAdvisorAi.ParseAdviceJson(validStructureJson);
        parsedStructure.IsSuccess.ShouldBeTrue();
        parsedStructure.Value.Changes.ShouldHaveSingleItem().Kind.ShouldBe(ProposedChangeKind.CreateCategory);
    }
}
