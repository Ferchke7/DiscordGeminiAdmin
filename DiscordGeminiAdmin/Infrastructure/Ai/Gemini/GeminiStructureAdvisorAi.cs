using System.Text;
using System.Text.Json;
using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Domain.Structure;
using FluentResults;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;
using Type = Google.GenAI.Types.Type;

namespace DiscordGeminiAdmin.Infrastructure.Ai.Gemini;

internal sealed class GeminiStructureAdvisorAi(
    Client client,
    IOptions<GeminiSettings> options,
    ILogger<GeminiStructureAdvisorAi> logger) : IStructureAdvisorAi
{
    private const string SystemPrompt = """
        Ты — умный ИИ-ассистент, старший программист и организатор Discord-сервера (Code-Guru).
        Ты умеешь выполнять 4 типа задач в зависимости от запроса пользователя:

        1. НАПИСАТЬ ИЛИ РАЗОБРАТЬ КОД:
           Если просят написать код, функцию, алгоритм, SQL-запрос, исправить баг или сделать код-ревью — напиши в поле `summary` подробный ответ с качественным кодом в блоках Markdown (например ```csharp, ```java, ```python, ```js), а массив `changes` оставь ПУСТЫМ (`[]`).

        2. ОТВЕТИТЬ НА ВОПРОС:
           Если пользователь задаёт вопрос по программированию, технологиям, серверу или любой теме — дай чёткий, развёрнутый ответ в поле `summary`, а массив `changes` оставь ПУСТЫМ (`[]`).

        3. НАЙТИ В ИСТОРИИ ЧАТА ИЛИ СДЕЛАТЬ ВЫЖИМКУ:
           Если пользователь просит найти что-то в истории переписки («найди в истории...», «кто писал про...», «что обсуждали в чате», «сделай саммари») — внимательно изучи блок `<channel_history>`, укажи кто, когда и что именно писал, дай ответ в поле `summary`, а массив `changes` оставь ПУСТЫМ (`[]`).

        4. ОРГАНИЗОВАТЬ СЕРВЕР (ГОЛОСОВЫЕ, ГРУППЫ, КАНАЛЫ, РОЛИ):
           ТОЛЬКО если пользователь просит создать/организовать голосовые комнаты, группы (категории), текстовые каналы, роли или поднять структуру сервера под тематику — заполни массив `changes` нужными шагами:
           - CreateCategory (создать группу/категорию каналов, например: "📌 ИНФОРМАЦИЯ", "💻 РАЗРАБОТКА", "🔊 ГОЛОСОВЫЕ КОМНАТЫ")
           - CreateVoiceChannel (создать голосовой канал; ОБЯЗАТЕЛЬНО указывай parentCategory — название группы/категории)
           - CreateTextChannel (создать текстовый канал; указывай parentCategory — группу/категорию, и newNameOrTopic — краткое описание канала)
           - UpdateChannelTopic (обновить описание существующего текстового канала)
           - RenameChannel (переименовать существующий канал; в newNameOrTopic укажи новое имя)
           - CreateRole (создать роль на сервере)
           Не дублируй категории, каналы и роли, которые уже есть на сервере.
           Всегда пиши `summary` на русском языке.
        """;

    private static readonly Schema AdviceSchema = new()
    {
        Type = Type.Object,
        Properties = new Dictionary<string, Schema>
        {
            ["summary"] = new() { Type = Type.String },
            ["changes"] = new()
            {
                Type = Type.Array,
                Items = new Schema
                {
                    Type = Type.Object,
                    Properties = new Dictionary<string, Schema>
                    {
                        ["kind"] = new() { Type = Type.String },
                        ["targetName"] = new() { Type = Type.String },
                        ["parentCategory"] = new() { Type = Type.String },
                        ["newNameOrTopic"] = new() { Type = Type.String },
                        ["rationale"] = new() { Type = Type.String }
                    },
                    Required = ["kind", "targetName", "rationale"]
                }
            }
        },
        Required = ["summary", "changes"]
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<StructureAdviceDto>> ProposeStructureAsync(
        GuildStructureContext context,
        CancellationToken ct)
    {
        var model = options.Value.Models.Structure;
        if (string.IsNullOrWhiteSpace(model))
            return Result.Fail("Не задана модель организатора Gemini:Models:Structure");

        try
        {
            var response = await client.Models.GenerateContentAsync(
                model: model,
                contents: BuildPrompt(context),
                config: new GenerateContentConfig
                {
                    SystemInstruction = new Content { Parts = [new Part { Text = SystemPrompt }] },
                    Temperature = 0.2f,
                    ResponseMimeType = "application/json",
                    ResponseSchema = AdviceSchema
                });

            var rawJson = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            return ParseAdviceJson(rawJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка генерации плана структуры для гильдии {GuildId}", context.GuildId.Value);
            return Result.Fail($"Не удалось сгенерировать план структуры через Gemini ({model}): {ex.Message}");
        }
    }

    internal static Result<StructureAdviceDto> ParseAdviceJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return Result.Fail("Пустой ответ от Gemini при генерации плана структуры");

        try
        {
            var raw = JsonSerializer.Deserialize<RawAdvice>(rawJson, JsonOptions);
            if (raw is null || string.IsNullOrWhiteSpace(raw.Summary) || raw.Changes is null)
                return Result.Fail("Некорректный JSON плана структуры");

            var changes = new List<ProposedChange>();
            foreach (var c in raw.Changes)
            {
                if (string.IsNullOrWhiteSpace(c.TargetName) || string.IsNullOrWhiteSpace(c.Rationale))
                    continue;
                if (!Enum.TryParse<ProposedChangeKind>(c.Kind, ignoreCase: true, out var kind))
                    continue;

                changes.Add(new ProposedChange(
                    kind,
                    c.TargetName.Trim(),
                    string.IsNullOrWhiteSpace(c.ParentCategory) ? null : c.ParentCategory.Trim(),
                    string.IsNullOrWhiteSpace(c.NewNameOrTopic) ? null : c.NewNameOrTopic.Trim(),
                    c.Rationale.Trim()));
            }

            return Result.Ok(new StructureAdviceDto(raw.Summary.Trim(), changes));
        }
        catch (JsonException)
        {
            return Result.Fail("Невалидный JSON плана структуры от Gemini");
        }
    }

    private static string BuildPrompt(GuildStructureContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Сервер: {context.CurrentStructure.GuildName}");
        sb.AppendLine($"Тематика: {context.ServerTheme}");
        if (!string.IsNullOrWhiteSpace(context.AdditionalGoal))
            sb.AppendLine($"Пожелание администратора: {context.AdditionalGoal}");

        sb.AppendLine("Текущие категории: " + string.Join(", ", context.CurrentStructure.Categories));
        sb.AppendLine("Текущие каналы:");
        foreach (var ch in context.CurrentStructure.Channels)
        {
            sb.AppendLine($"- #{ch.Name} (категория: {ch.CategoryName ?? "нет"}, голосовой: {ch.IsVoice}, тема: {ch.Topic ?? "нет"})");
        }
        sb.AppendLine("Текущие роли: " + string.Join(", ", context.CurrentStructure.Roles));

        if (context.RecentChannelMessages is { Count: > 0 })
        {
            sb.AppendLine("<channel_history>");
            foreach (var msg in context.RecentChannelMessages)
                sb.AppendLine(msg);
            sb.AppendLine("</channel_history>");
        }

        return sb.ToString();
    }

    private sealed record RawAdvice(string? Summary, List<RawChange>? Changes);

    private sealed record RawChange(
        string? Kind,
        string? TargetName,
        string? ParentCategory,
        string? NewNameOrTopic,
        string? Rationale);
}
