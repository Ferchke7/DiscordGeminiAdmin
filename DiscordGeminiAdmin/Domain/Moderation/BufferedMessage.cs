using DiscordGeminiAdmin.SharedKernel;

namespace DiscordGeminiAdmin.Domain.Moderation;

/// <summary>Сообщение из буфера часового анализа (хранится не дольше Retention:MessageBufferHours).</summary>
public sealed class BufferedMessage(Guid id) : Entity<Guid>(id)
{
    public required GuildId GuildId { get; init; }
    public required ChannelId ChannelId { get; init; }
    public required MemberId AuthorId { get; init; }
    public required MessageId DiscordMessageId { get; init; }
    public required string AuthorName { get; init; }
    public required string Content { get; init; }
    public required DateTime CreatedAt { get; init; }

    public static BufferedMessage Create(
        GuildId guildId,
        ChannelId channelId,
        MemberId authorId,
        MessageId discordMessageId,
        string authorName,
        string content,
        DateTime createdAt) =>
        new(Guid.CreateVersion7())
        {
            GuildId = guildId,
            ChannelId = channelId,
            AuthorId = authorId,
            DiscordMessageId = discordMessageId,
            AuthorName = authorName.Length > 100 ? authorName[..100] : authorName,
            Content = content.Length > 2000 ? content[..2000] : content,
            CreatedAt = createdAt
        };
}
