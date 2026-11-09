using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.News.PublishNews;

public sealed record PublishNews(
    GuildId GuildId,
    ChannelId? TargetChannelId = null,
    string? Topic = null,
    bool InNewThread = true,
    string? CustomThreadTitle = null);

public static class PublishNewsUseCase
{
    public static async ValueTask<Result<NewsPublicationResult>> Handle(
        PublishNews command,
        INewsGeneratorAi newsAi,
        IGuildGateway guildGateway,
        IGuildProfileRepository guildProfiles,
        CancellationToken cancellationToken)
    {
        ChannelId targetChannelId;
        if (command.TargetChannelId.HasValue)
        {
            targetChannelId = command.TargetChannelId.Value;
        }
        else
        {
            var channelResult = await guildGateway.EnsureNewsChannelAsync(
                command.GuildId,
                "новости",
                "📰 ИНФО И НОВОСТИ",
                cancellationToken);

            if (channelResult.IsFailed)
                return channelResult.ToResult();

            targetChannelId = channelResult.Value;
        }

        var profile = await guildProfiles.GetByGuildIdAsync(command.GuildId, cancellationToken);
        var effectiveTopic = !string.IsNullOrWhiteSpace(command.Topic)
            ? command.Topic.Trim()
            : profile?.Theme.Value;

        var digestResult = await newsAi.GenerateNewsDigestAsync(effectiveTopic, cancellationToken);
        if (digestResult.IsFailed)
            return digestResult.ToResult();

        return await guildGateway.PublishNewsDigestAsync(
            command.GuildId,
            targetChannelId,
            digestResult.Value,
            command.InNewThread,
            command.CustomThreadTitle,
            cancellationToken);
    }
}
