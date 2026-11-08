using FluentResults;
using NetCord;
using NetCord.Rest;

namespace DiscordGeminiAdmin.Presentation.Discord.Components;

internal static class EmbedTheme
{
    public const int Primary = 0x5865F2;
    public const int Success = 0x2ECC71;
    public const int Warning = 0xF39C12;
    public const int Danger = 0xE74C3C;
    public const int Purple = 0x9B59B6;
    public const int Cyan = 0x00CEC9;

    public const string FooterText = "🤖 Gemini AI Moderator & Organizer • Clean Architecture";

    public static InteractionMessageProperties Card(
        string title,
        string description,
        int color = Primary,
        IEnumerable<EmbedFieldProperties>? fields = null,
        IEnumerable<IMessageComponentProperties>? components = null,
        bool ephemeral = false)
    {
        var embed = new EmbedProperties
        {
            Title = title,
            Description = description,
            Color = new Color(color),
            Timestamp = DateTimeOffset.UtcNow,
            Fields = fields?.ToArray(),
            Footer = new EmbedFooterProperties { Text = FooterText }
        };

        return new InteractionMessageProperties
        {
            Embeds = [embed],
            Components = components?.ToArray(),
            Flags = ephemeral ? MessageFlags.Ephemeral : default
        };
    }

    public static InteractionMessageProperties SuccessCard(
        string title,
        string description,
        IEnumerable<EmbedFieldProperties>? fields = null,
        bool ephemeral = false) =>
        Card($"✅ {title}", description, Success, fields, ephemeral: ephemeral);

    public static InteractionMessageProperties ErrorCard(ResultBase result, string title = "Ошибка выполнения")
    {
        var reasons = result.Errors
            .Select(e =>
            {
                var code = e.Metadata.TryGetValue("Code", out var c) && c is not null ? $" (`{c}`)" : string.Empty;
                return $"• {e.Message}{code}";
            })
            .ToArray();

        var description = reasons.Length > 0
            ? string.Join("\n", reasons)
            : "Неизвестная ошибка при выполнении операции.";

        return Card(
            $"❌ {title}",
            description,
            Danger,
            [
                new EmbedFieldProperties
                {
                    Name = "💡 Что проверить",
                    Value = "Убедитесь, что сервер настроен (`/mod setup` или `/mod-config set-theme`), а роль бота находится выше ролей участников.",
                    Inline = false
                }
            ],
            ephemeral: false);
    }

    public static InteractionMessageProperties ErrorCard(string message, string title = "Ошибка выполнения") =>
        Card($"❌ {title}", $"**Причина:** {message}", Danger, ephemeral: false);

    public static InteractionMessageProperties ExceptionCard(Exception ex, string title = "Сбой при выполнении команды")
    {
        var root = ex.InnerException ?? ex;
        var hint = root switch
        {
            RestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
                "У бота недостаточно прав в Discord (403 Forbidden). Поднимите роль `CodeGuru` выше в настройках сервера («Роли») и выдайте право «Администратор».",
            RestException { StatusCode: System.Net.HttpStatusCode.BadRequest } restEx =>
                $"Discord отклонил запрос (400 Bad Request): `{restEx.Message}`",
            _ when root.Message.Contains("429", StringComparison.OrdinalIgnoreCase) || root.Message.Contains("quota", StringComparison.OrdinalIgnoreCase) =>
                "Превышен лимит запросов к Gemini API (Rate Limit / Quota). Подождите 1 минуту и повторите попытку.",
            _ when root.Message.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) || root.Message.Contains("API key", StringComparison.OrdinalIgnoreCase) =>
                "Некорректный или заблокированный `GEMINI_API_KEY` в настройках окружения.",
            _ => "Подробности записаны в системный журнал контейнера `dga-bot`."
        };

        var msg = root.Message.Length > 500 ? root.Message[..500] + "..." : root.Message;

        return Card(
            $"❌ {title}",
            $"**Из-за чего произошла ошибка:**\n```{msg}```",
            Danger,
            [
                new EmbedFieldProperties { Name = "⚙️ Тип исключения", Value = $"`{root.GetType().Name}`", Inline = true },
                new EmbedFieldProperties { Name = "🛠️ Как исправить", Value = hint, Inline = false }
            ],
            ephemeral: false);
    }

    public static MessageProperties ToChannelMessage(InteractionMessageProperties card) =>
        new()
        {
            Embeds = card.Embeds,
            Components = card.Components
        };
}
