using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Guilds;

/// <summary>Профиль и настройки модерации конкретного Discord-сервера.</summary>
public sealed class GuildProfile(Guid id) : AggregationRoot<Guid>(id)
{
    public required GuildId GuildId { get; init; }
    public required ServerTheme Theme { get; set; }
    public required ChannelId ModChannelId { get; set; }
    public required ModerationPolicy ModerationPolicy { get; set; }
    public required DateTime UpdatedAt { get; set; }

    public static GuildProfile Create(
        GuildId guildId,
        ServerTheme theme,
        ChannelId modChannelId,
        ModerationPolicy policy,
        DateTime now) =>
        new(Guid.CreateVersion7())
        {
            GuildId = guildId,
            Theme = theme,
            ModChannelId = modChannelId,
            ModerationPolicy = policy,
            UpdatedAt = now
        };

    public Result UpdateConfiguration(
        ServerTheme theme,
        ChannelId modChannelId,
        ModerationPolicy policy,
        DateTime now)
    {
        if (policy.MinConfidence is < 0.5 or > 1.0)
            return Result.Fail(GuildErrors.InvalidSettings("Порог уверенности авто-бана должен быть в диапазоне 0.5..1.0"));
        if (policy.MinEvidenceMessages < 1)
            return Result.Fail(GuildErrors.InvalidSettings("Минимум сообщений-доказательств не может быть меньше 1"));
        if (policy.MaxAutoBansPerHour < 0)
            return Result.Fail(GuildErrors.InvalidSettings("Часовой лимит авто-банов не может быть отрицательным"));

        Theme = theme;
        ModChannelId = modChannelId;
        ModerationPolicy = policy;
        UpdatedAt = now;
        return Result.Ok();
    }
}
