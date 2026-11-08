using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Knowledge;

/// <summary>Документ базы знаний гильдии (правила, описание тематики, гайд по каналам, прецеденты).</summary>
public sealed class KnowledgeDocument(Guid id) : AggregationRoot<Guid>(id)
{
    public required GuildId GuildId { get; init; }
    public required RuleCode RuleCode { get; init; }
    public required KnowledgeSourceType SourceType { get; init; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public required ContentHash ContentHash { get; set; }
    public required DateTime UpdatedAt { get; set; }

    public static Result<KnowledgeDocument> Create(
        GuildId guildId,
        RuleCode ruleCode,
        KnowledgeSourceType sourceType,
        string title,
        string content,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Result.Fail(KnowledgeErrors.EmptyDocument);
        if (content.Length > KnowledgeErrors.MaxContentLength)
            return Result.Fail(KnowledgeErrors.DocumentTooLarge);

        return new KnowledgeDocument(Guid.CreateVersion7())
        {
            GuildId = guildId,
            RuleCode = ruleCode,
            SourceType = sourceType,
            Title = title.Trim(),
            Content = content.Trim(),
            ContentHash = ContentHash.Compute(content),
            UpdatedAt = now
        };
    }

    /// <summary>Обновляет текст документа. Возвращает false в Value, если ContentHash не изменился.</summary>
    public Result<bool> TryUpdateContent(string title, string content, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Result.Fail(KnowledgeErrors.EmptyDocument);
        if (content.Length > KnowledgeErrors.MaxContentLength)
            return Result.Fail(KnowledgeErrors.DocumentTooLarge);

        var newHash = ContentHash.Compute(content);
        if (newHash == ContentHash)
            return Result.Ok(false);

        Title = title.Trim();
        Content = content.Trim();
        ContentHash = newHash;
        UpdatedAt = now;
        return Result.Ok(true);
    }
}
