using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Knowledge.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Knowledge.Manage;

public sealed record ListKnowledgeDocuments(GuildId GuildId);
public sealed record DeleteKnowledgeDocument(GuildId GuildId, string RuleCode);

public sealed class ManageKnowledgeUseCase(
    IKnowledgeDocumentRepository documents,
    IKnowledgeIndex knowledgeIndex,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<IReadOnlyList<KnowledgeDocument>>> Handle(
        ListKnowledgeDocuments query,
        CancellationToken ct)
    {
        var items = await documents.GetAllByGuildAsync(query.GuildId, ct);
        return Result.Ok(items);
    }

    public async Task<Result> Handle(DeleteKnowledgeDocument command, CancellationToken ct)
    {
        if (!RuleCode.TryFrom(command.RuleCode.Trim(), out var ruleCode))
            return Result.Fail(KnowledgeErrors.EmptyDocument);

        var existing = await documents.GetByRuleCodeAsync(command.GuildId, ruleCode, ct);
        if (existing is null)
            return Result.Fail($"Документ с кодом `{command.RuleCode}` не найден в базе знаний сервера.");

        await knowledgeIndex.ReplaceDocumentChunksAsync(existing.Id, command.GuildId, [], ct);
        documents.Remove(existing);
        await unitOfWork.SaveAsync(ct);
        return Result.Ok();
    }
}
