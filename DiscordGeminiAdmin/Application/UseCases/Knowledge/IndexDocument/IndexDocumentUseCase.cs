using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Knowledge.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Knowledge.IndexDocument;

public sealed record IndexDocument(
    GuildId GuildId,
    string RuleCode,
    KnowledgeSourceType SourceType,
    string Title,
    string Content);

public sealed record IndexDocumentResult(Guid DocumentId, int ChunksIndexed, bool WasSkippedUnchanged);

public static class IndexDocumentUseCase
{
    public static async ValueTask<Result<IndexDocumentResult>> Handle(
        IndexDocument command,
        IUnitOfWork unitOfWork,
        IKnowledgeDocumentRepository documents,
        ITextChunker chunker,
        IEmbeddingService embeddings,
        IKnowledgeIndex knowledgeIndex,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!RuleCode.TryFrom(command.RuleCode, out var ruleCode))
            return Result.Fail(KnowledgeErrors.EmptyDocument);

        var existing = await documents.GetByRuleCodeAsync(command.GuildId, ruleCode, cancellationToken);
        KnowledgeDocument document;

        if (existing is null)
        {
            var created = KnowledgeDocument.Create(
                command.GuildId, ruleCode, command.SourceType, command.Title, command.Content, now);
            if (created.IsFailed)
                return created.ToResult();

            document = created.Value;
            documents.Add(document);
        }
        else
        {
            var updateResult = existing.TryUpdateContent(command.Title, command.Content, now);
            if (updateResult.IsFailed)
                return updateResult.ToResult();

            // Идемпотентная переиндексация: если SHA-256 не изменился, не тратим квоту Gemini Embeddings.
            if (!updateResult.Value)
                return Result.Ok(new IndexDocumentResult(existing.Id, 0, WasSkippedUnchanged: true));

            document = existing;
        }

        var textChunks = chunker.Chunk(document.RuleCode.Value, document.Title, document.Content);
        var writeChunks = new List<KnowledgeChunkWriteDto>(textChunks.Count);

        foreach (var chunk in textChunks)
        {
            var embResult = await embeddings.EmbedDocumentAsync($"{chunk.Title}\n{chunk.Content}", cancellationToken);
            if (embResult.IsFailed)
                return embResult.ToResult();

            writeChunks.Add(new KnowledgeChunkWriteDto(
                document.Id,
                document.GuildId,
                chunk.SubRuleCode,
                document.SourceType,
                chunk.Title,
                chunk.Content,
                embResult.Value));
        }

        await knowledgeIndex.ReplaceDocumentChunksAsync(document.Id, document.GuildId, writeChunks, cancellationToken);
        await unitOfWork.SaveAsync(cancellationToken);

        return Result.Ok(new IndexDocumentResult(document.Id, writeChunks.Count, WasSkippedUnchanged: false));
    }
}
