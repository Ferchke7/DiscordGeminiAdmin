using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.News.SetupNews;

public sealed record SetupNewsChannel(
    GuildId GuildId,
    string? ChannelName = "новости",
    string? CategoryName = "📰 ИНФО И НОВОСТИ");

public static class SetupNewsChannelUseCase
{
    public static async ValueTask<Result<ChannelId>> Handle(
        SetupNewsChannel command,
        IGuildGateway guildGateway,
        CancellationToken cancellationToken)
    {
        return await guildGateway.EnsureNewsChannelAsync(
            command.GuildId,
            command.ChannelName,
            command.CategoryName,
            cancellationToken);
    }
}
