using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.Dependencies.Ai;

public sealed record MemberMessageSampleDto(MessageId MessageId, string Content, DateTime SentAt);

public sealed record MemberBatchInputDto(
    MemberId MemberId,
    string AuthorName,
    IReadOnlyList<MemberMessageSampleDto> Messages);

public sealed record GuildModerationContext(
    GuildId GuildId,
    string ServerTheme,
    IReadOnlyList<KnowledgeHitDto> RulesAndPrecedents,
    IReadOnlyList<MemberBatchInputDto> MemberBatches);

public sealed record MemberAssessmentDto(
    MemberId MemberId,
    double Confidence,
    string Reason,
    string? RuleCode,
    IReadOnlyList<MessageId> EvidenceMessageIds);

public sealed record KnowledgeAnswerDto(string Answer, IReadOnlyList<string> CitedRuleCodes);

/// <summary>Порт ИИ для модерации сообщений и ответов по базе знаний (Gemini).</summary>
public interface IModerationAi
{
    Task<Result<IReadOnlyList<MemberAssessmentDto>>> AssessBatchAsync(
        GuildModerationContext context,
        CancellationToken ct);

    Task<Result<KnowledgeAnswerDto>> AnswerQuestionAsync(
        string serverTheme,
        string question,
        IReadOnlyList<KnowledgeHitDto> retrievedChunks,
        CancellationToken ct);
}

public sealed record GuildStructureContext(
    GuildId GuildId,
    string ServerTheme,
    string? AdditionalGoal,
    GuildStructureSnapshot CurrentStructure,
    IReadOnlyList<KnowledgeHitDto> KnowledgeContext);

public sealed record StructureAdviceDto(
    string Summary,
    IReadOnlyList<ProposedChange> Changes);

/// <summary>Порт ИИ-организатора для предложения улучшений структуры сервера по тематике.</summary>
public interface IStructureAdvisorAi
{
    Task<Result<StructureAdviceDto>> ProposeStructureAsync(
        GuildStructureContext context,
        CancellationToken ct);
}
