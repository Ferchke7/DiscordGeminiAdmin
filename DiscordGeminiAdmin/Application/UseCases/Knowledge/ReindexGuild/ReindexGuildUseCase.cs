using System.Text;
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.IndexDocument;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using Wolverine;

namespace DiscordGeminiAdmin.Application.UseCases.Knowledge.ReindexGuild;

public sealed record ReindexGuild(GuildId GuildId);

public static class ReindexGuildUseCase
{
    public static async ValueTask<Result<IndexDocumentResult>> Handle(
        ReindexGuild command,
        IGuildProfileRepository guilds,
        IGuildGateway gateway,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var profile = await guilds.GetByGuildIdAsync(command.GuildId, cancellationToken);
        if (profile is null)
            return Result.Fail(GuildErrors.NotConfigured());

        var structureResult = await gateway.GetGuildStructureAsync(command.GuildId, cancellationToken);
        if (structureResult.IsFailed)
            return structureResult.ToResult();

        var snapshot = structureResult.Value;
        var sb = new StringBuilder();
        sb.AppendLine($"# Структура и каналы сервера {snapshot.GuildName}");
        sb.AppendLine($"Тематика: {profile.Theme.Value}");
        sb.AppendLine();

        foreach (var channel in snapshot.Channels.Where(c => !c.IsVoice))
        {
            var cat = string.IsNullOrWhiteSpace(channel.CategoryName) ? "Без категории" : channel.CategoryName;
            var topic = string.IsNullOrWhiteSpace(channel.Topic) ? "описание не задано" : channel.Topic;
            sb.AppendLine($"## Канал #{channel.Name} ({cat})");
            sb.AppendLine(topic);
            sb.AppendLine();
        }

        return await bus.InvokeAsync<Result<IndexDocumentResult>>(
            new IndexDocument.IndexDocument(
                command.GuildId,
                RuleCode: "channels#guide",
                SourceType: KnowledgeSourceType.ChannelGuide,
                Title: $"Путеводитель по каналам {snapshot.GuildName}",
                Content: sb.ToString()),
            cancellationToken);
    }
}
