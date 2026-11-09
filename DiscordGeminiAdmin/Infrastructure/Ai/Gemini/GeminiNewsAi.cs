using System.Text.Json;
using DiscordGeminiAdmin.Application.Dependencies.Ai;
using FluentResults;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordGeminiAdmin.Infrastructure.Ai.Gemini;

internal sealed class GeminiNewsAi(
    Client client,
    IOptions<GeminiSettings> options,
    ILogger<GeminiNewsAi> logger) : INewsGeneratorAi
{
    private const string SystemPrompt = """
        Ты — главный редактор новостей передового IT и Discord-сообщества (Code-Guru News).
        Твоя задача — находить самые интересные, свежие и резонансные события из мира технологий, разработки ПО, ИИ (AI/ML), кибербезопасности, фреймворков и науки.

        Формат ответа СТРОГО в виде валидного JSON-объекта со следующей структурой:
        {
          "headline": "Яркий заголовок выпуска новостей с эмодзи (например: 📰 Главные IT & AI новости: релизы, прорывы и тренды)",
          "introduction": "Краткое бодрое вступление на 1-2 предложения, задающее тон выпуску.",
          "items": [
            {
              "title": "Заголовок новости с эмодзи (например: 🚀 Вышел .NET 10 с революционной производительностью)",
              "summary": "Суть события на 2-4 предложения: что произошло, какие ключевые новшества или изменения появились.",
              "significance": "Почему это важно разработчикам, инженерам и комьюнити (1-2 предложения).",
              "sourceUrl": "Ссылка на источник если найдена в поиске (или null)"
            }
          ],
          "discussionQuestion": "Провокационный или интересный вопрос к комьюнити для обсуждения новости в ветке чата."
        }

        Правила:
        1. Сгенерируй от 3 до 5 самых качественных и актуальных новостей.
        2. Пиши живым, профессиональным и увлекательным языком на русском языке.
        3. Не выдумывай ложные факты; используй данные из поиска.
        4. Ответ должен содержать ТОЛЬКО JSON без лишнего текста вокруг.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<NewsDigestDto>> GenerateNewsDigestAsync(
        string? themeOrTopic,
        CancellationToken ct)
    {
        var model = options.Value.Models.News;
        if (string.IsNullOrWhiteSpace(model))
            model = options.Value.Models.Structure;

        var topicDesc = !string.IsNullOrWhiteSpace(themeOrTopic)
            ? $"Тематика для подборки: {themeOrTopic}."
            : "Тематика для подборки: IT, искусственный интеллект, программирование, опенсорс и современные технологии.";

        var prompt = $"""
            {topicDesc}
            Найди самые свежие, горячие и важные новости за последние дни с помощью Google Search.
            Выбери самые интересные события и сформируй структурированный дайджест в формате JSON.
            """;

        try
        {
            var response = await client.Models.GenerateContentAsync(
                model: model,
                contents: prompt,
                config: new GenerateContentConfig
                {
                    SystemInstruction = new Content { Parts = [new Part { Text = SystemPrompt }] },
                    Temperature = 0.3f,
                    Tools = [new Tool { GoogleSearch = new GoogleSearch() }],
                    ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 }
                });

            var text = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            var parseResult = ParseDigestJson(text);
            if (parseResult.IsSuccess)
                return parseResult;

            logger.LogWarning("Не удалось распарсить JSON с поисковым заземлением, пробуем резервную генерацию...");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ошибка при генерации новостей с Google Search, пробуем без инструментов");
        }

        try
        {
            var fallbackResponse = await client.Models.GenerateContentAsync(
                model: model,
                contents: prompt,
                config: new GenerateContentConfig
                {
                    SystemInstruction = new Content { Parts = [new Part { Text = SystemPrompt }] },
                    Temperature = 0.4f,
                    ResponseMimeType = "application/json",
                    ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 }
                });

            var fallbackText = fallbackResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            return ParseDigestJson(fallbackText);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при резервной генерации новостей Gemini");
            return Result.Fail($"Ошибка генерации новостей Gemini: {ex.Message}");
        }
    }

    internal static Result<NewsDigestDto> ParseDigestJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Fail("Пустой ответ от Gemini при генерации новостей");

        try
        {
            var clean = CleanJson(raw);
            var parsed = JsonSerializer.Deserialize<RawNewsDigest>(clean, JsonOptions);
            if (parsed is null || parsed.Items is null || parsed.Items.Count == 0)
                return Result.Fail("Не удалось извлечь новости из ответа ИИ");

            var items = parsed.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Title) && !string.IsNullOrWhiteSpace(i.Summary))
                .Select(i => new NewsItemDto(
                    i.Title!.Trim(),
                    i.Summary!.Trim(),
                    string.IsNullOrWhiteSpace(i.Significance) ? "Важное событие в индустрии" : i.Significance.Trim(),
                    string.IsNullOrWhiteSpace(i.SourceUrl) ? null : i.SourceUrl.Trim()))
                .ToList();

            if (items.Count == 0)
                return Result.Fail("Отсутствуют валидные элементы новостей в ответе ИИ");

            var headline = !string.IsNullOrWhiteSpace(parsed.Headline)
                ? parsed.Headline.Trim()
                : "📰 Свежий дайджест IT & Tech новостей";

            var intro = !string.IsNullOrWhiteSpace(parsed.Introduction)
                ? parsed.Introduction.Trim()
                : "Главные события и технологические обновления индустрии к этому часу.";

            var question = !string.IsNullOrWhiteSpace(parsed.DiscussionQuestion)
                ? parsed.DiscussionQuestion.Trim()
                : "Какая новость заинтересовала вас больше всего? Делитесь мнением в ветке!";

            return Result.Ok(new NewsDigestDto(headline, intro, items, question));
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Невалидный JSON дайджеста новостей: {ex.Message}");
        }
    }

    private static string CleanJson(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["```json".Length..];
        else if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[3..];

        if (trimmed.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3];

        return trimmed.Trim();
    }

    private sealed record RawNewsDigest(
        string? Headline,
        string? Introduction,
        List<RawNewsItem>? Items,
        string? DiscussionQuestion);

    private sealed record RawNewsItem(
        string? Title,
        string? Summary,
        string? Significance,
        string? SourceUrl);
}
