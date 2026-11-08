using DiscordGeminiAdmin.Application.UseCases.Moderation.IngestMessage;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Events;

/// <summary>Буферизует входящие сообщения пользователей гильдий для последующего часового ИИ-анализа.</summary>
internal sealed class MessageCreateGatewayHandler(IServiceScopeFactory scopeFactory) : IMessageCreateGatewayHandler
{
    public async ValueTask HandleAsync(Message message)
    {
        if (message.Author.IsBot || message.GuildId is null || string.IsNullOrWhiteSpace(message.Content))
            return;

        if (!GuildId.TryFrom(message.GuildId.Value, out var guildId)
            || !ChannelId.TryFrom(message.ChannelId, out var channelId)
            || !MemberId.TryFrom(message.Author.Id, out var authorId)
            || !MessageId.TryFrom(message.Id, out var discordMessageId))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync<Result>(new IngestMessage(
            guildId,
            channelId,
            authorId,
            discordMessageId,
            message.Author.Username,
            message.Content));
    }
}
