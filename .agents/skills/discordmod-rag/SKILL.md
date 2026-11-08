---
name: discordmod-rag
description: >-
  Use when writing, changing or reviewing the RAG and Knowledge subsystem in DiscordGeminiAdmin: PostgreSQL + pgvector with EF Core (Pgvector.EntityFrameworkCore), Gemini embeddings (EmbedContentAsync), markdown/rule chunking, content_hash idempotent reindexing, strict guild_id isolation, and storing moderator decisions as precedents. Do NOT use for ModerationPolicy rules (discordmod-domain) or Discord slash commands (discordmod-discord).
---

# discordmod-rag

Стек RAG: **PostgreSQL (`pgvector/pgvector:pg17`) + `Pgvector.EntityFrameworkCore` (`0.3.0`) + `Google.GenAI` (`EmbedContentAsync`)**.

## Non-negotiables

1. **Строгая изоляция по `GuildId`.** Каждый чанк в таблице знаний хранит `guild_id`. Любой векторный запрос (`CosineDistance`) **обязан** начинаться с `.Where(c => c.GuildId == guildId)` до `OrderBy(c => c.Embedding.CosineDistance(queryVector))`. *Prevents:* утечку правил/прецедентов одного Discord-сервера в другой.
2. **Размерность и модель эмбеддингов — из конфигурации** (`Gemini:Models:Embedding`, `Gemini:EmbeddingDimensions`, по умолчанию `768`). В `EmbedContentConfig` передавай `OutputDimensionality = options.EmbeddingDimensions` и `TaskType = "RETRIEVAL_DOCUMENT"` при индексации / `"RETRIEVAL_QUERY"` при поиске. *Prevents:* несовпадение размерности вектора с колонкой `vector(768)`.
3. **Идемпотентная переиндексация по `ContentHash` (SHA-256).** Перед вызовом Gemini Embeddings считай SHA-256 нормализованного текста документа. Если `ContentHash` не изменился — не трать квоту API и не пересчитывай чанки. *Prevents:* лишние расходы на `/kb reindex`.
4. **Структурный чанкинг с идентификаторами правил.**
   - Режь документы по заголовкам/пунктам правил (`#`, `##`, нумерованные пункты), целевой размер чанка — 300–500 токенов (до ~1500 символов) с перекрытием 100 символов.
   - У каждого чанка есть стабильный `RuleCode` / `ChunkKey` (например, `rules#3`, `faq#2`, `precedent:0195...`), который передаётся в промпт Gemini и возвращается как `RuleReference`.
   *Prevents:* потерю контекста правила и невозможность сослаться на конкретный пункт при авто-бане.
5. **Обучение на решениях модераторов (прецеденты).** Когда модератор нажимает `[Бан]`, `[Игнор]` или `[Разбанить]`, обработчик доменного события сохраняет краткий обезличенный прецедент (`SourceType = KnowledgeSourceType.Precedent`: категория + причина + вердикт человека, без сырых ПДн) в индекс гильдии.
6. **HNSW-индекс в EF Core.** На колонку `Embedding` (`vector(768)`) настраивай индекс `HasMethod("hnsw").HasOperators("vector_cosine_ops")`.

## Порты в `Application/Dependencies/Knowledge/`

```csharp
public interface IEmbeddingService
{
    Task<Result<float[]>> EmbedDocumentAsync(string text, CancellationToken ct);
    Task<Result<float[]>> EmbedQueryAsync(string query, CancellationToken ct);
}

public interface IKnowledgeIndex
{
    Task<IReadOnlyList<KnowledgeHitDto>> SearchAsync(
        GuildId guildId,
        float[] queryVector,
        int topK,
        CancellationToken ct);
}
```

## Шаблон: EF Core конфигурация чанка с `pgvector`

```csharp
using DiscordGeminiAdmin.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DiscordGeminiAdmin.Infrastructure.Persistence.Knowledge.EntityTypeConfigurations;

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.Property(x => x.Embedding).HasColumnType("vector(768)");
        builder.HasIndex(x => x.GuildId);
        builder.HasIndex(x => x.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");
    }
}
```

## Шаблон: генерация эмбеддинга через `Google.GenAI`

```csharp
var response = await client.Models.EmbedContentAsync(
    model: options.Value.Models.Embedding,
    contents: text,
    config: new EmbedContentConfig
    {
        OutputDimensionality = options.Value.EmbeddingDimensions,
        TaskType = isQuery ? "RETRIEVAL_QUERY" : "RETRIEVAL_DOCUMENT"
    });

var values = response.Embeddings?[0].Values?.Select(v => (float)v).ToArray();
```

## Шаблон: поиск top-k в `PgVectorKnowledgeIndex`

```csharp
var query = new Vector(queryVector);

return await db.KnowledgeChunks
    .AsNoTracking()
    .Where(c => c.GuildId == guildId)
    .OrderBy(c => c.Embedding!.CosineDistance(query))
    .Take(topK)
    .Select(c => new KnowledgeHitDto(c.RuleCode, c.Title, c.Content))
    .ToListAsync(ct);
```
