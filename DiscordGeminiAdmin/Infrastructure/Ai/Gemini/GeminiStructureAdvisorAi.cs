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
        Ты — практичный ИИ-организатор и строитель Discord-серверов.
        Твоя задача — по просьбе администратора (Пожелание администратора) или по тематике сервера сразу создать нужные группы (категории), голосовые каналы, текстовые каналы и роли.
        Допустимые значения kind:
        - CreateCategory (создать группу/категорию каналов, например: "📌 ИНФОРМАЦИЯ", "💻 РАЗРАБОТКА", "🔊 ГОЛОСОВЫЕ КОМНАТЫ")
        - CreateVoiceChannel (создать голосовой канал; ОБЯЗАТЕЛЬНО указывай parentCategory — название группы/категории, куда поместить голосовой канал, например "🔊 ГОЛОСОВЫЕ КОМНАТЫ")
        - CreateTextChannel (создать текстовый канал; указывай parentCategory — группу/категорию, и newNameOrTopic — краткое описание канала)
        - UpdateChannelTopic (обновить описание существующего текстового канала)
        - RenameChannel (переименовать существующий канал; в newNameOrTopic укажи новое имя)
        - CreateRole (создать роль на сервере под тематику или по просьбе админа)

        ПРАВИЛА:
        1. Если администратор просит организовать голосовые каналы — создай удобную группу (CreateCategory, если её ещё нет) и создай внутри неё продуманные голосовые каналы (CreateVoiceChannel с заполненным parentCategory).
        2. Если администратор просит поднять сервер с нуля или задать тематику — создай полноценный набор групп (категорий), текстовых каналов, голосовых каналов и ролей под эту тематику.
        3. Если администратор просит добавить конкретные группы, каналы или роли — выполни ровно то, что он просит, плюс логично распредели каналы по группам (parentCategory).
        4. Не дублируй категории, каналы и роли, которые уже есть в списке текущих.
        5. Пиши summary и rationale кратко и по делу на русском языке.
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
