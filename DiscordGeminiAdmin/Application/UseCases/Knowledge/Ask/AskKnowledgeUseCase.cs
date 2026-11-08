using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Knowledge.Ask;

public sealed record AskKnowledge(GuildId GuildId, string Question);

public static class AskKnowledgeUseCase
{
    private const int DefaultTopK = 5;

    public static async ValueTask<Result<KnowledgeAnswerDto>> Handle(
        AskKnowledge command,
        IGuildProfileRepository guilds,
        IEmbeddingService embeddings,
        IKnowledgeIndex knowledgeIndex,
        IModerationAi moderationAi,
        CancellationToken cancellationToken)
    {
        var profile = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        if (profile is null)
            return Result.Fail(GuildErrors.NotConfigured());

        var queryEmbedding = await embeddings.EmbedQueryAsync(command.Question, cancellationToken);
        if (queryEmbedding.IsFailed)
            return queryEmbedding.ToResult();

        var hits = await knowledgeIndex.SearchAsync(
            command.GuildId, queryEmbedding.Value, DefaultTopK, cancellationToken);

        return await moderationAi.AnswerQuestionAsync(
            profile.Theme.Value, command.Question, hits, cancellationToken);
    }
}
