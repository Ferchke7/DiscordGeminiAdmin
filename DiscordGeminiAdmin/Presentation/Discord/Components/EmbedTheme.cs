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
        bool ephemeral = true)
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
        bool ephemeral = true) =>
        Card($"✅ {title}", description, Success, fields, ephemeral: ephemeral);

    public static InteractionMessageProperties ErrorCard(ResultBase result, string title = "Ошибка выполнения") =>
        Card(
            $"❌ {title}",
            string.Join("\n• ", result.Errors.Select(e => e.Message)),
            Danger,
            ephemeral: true);

    public static InteractionMessageProperties ErrorCard(string message, string title = "Ошибка выполнения") =>
        Card($"❌ {title}", message, Danger, ephemeral: true);
}
