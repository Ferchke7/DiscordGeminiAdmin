using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.UseCases.News.PublishNews;
using DiscordGeminiAdmin.Application.UseCases.News.SetupNews;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

/// <summary>
/// Команды для публикации свежих IT и тематических новостей,
/// создания веток обсуждения и настройки новостного канала.
/// </summary>
public sealed class NewsCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    private async ValueTask<bool> EnsureModeratorAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return false;
        }

        if (!await ModeratorGuard.IsModeratorAsync(Context.User, Context.Guild, Context.Guild.Id, Context.Client.Rest))
        {
            await RespondAsync(InteractionCallback.Message(ModeratorGuard.AccessDeniedCard()));
            return false;
        }

        return true;
    }

    [SlashCommand("news", "Собрать и опубликовать свежий дайджест интересных новостей с веткой обсуждения")]
    public async Task PublishNewsAsync(
        [SlashCommandParameter(Name = "topic", Description = "Тематика новостей (например: ИИ, .NET, геймдев, кибербезопасность, наука)")] string? topic = null,
        [SlashCommandParameter(Name = "thread", Description = "Создать ветку (Thread) под новостью для обсуждения")] bool thread = true,
        [SlashCommandParameter(Name = "in_current_channel", Description = "Опубликовать в этом канале (иначе в #новости)")] bool inCurrentChannel = false)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        ChannelId? targetChannelId = inCurrentChannel ? ChannelId.From(Context.Channel.Id) : null;
        var result = await bus.InvokeAsync<Result<NewsPublicationResult>>(new PublishNews(
            GuildId.From(Context.Guild!.Id),
            TargetChannelId: targetChannelId,
            Topic: topic,
            InNewThread: thread));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка публикации новостей"));
            return;
        }

        var pub = result.Value;
        var threadInfo = pub.ThreadId is not null
            ? $"\n💬 Создана ветка для обсуждения: <#{pub.ThreadId.Value}>"
            : string.Empty;

        var embed = new EmbedProperties
        {
            Title = "📰 Новости успешно опубликованы!",
            Description = $"Дайджест опубликован в канале <#{pub.ChannelId.Value}>.{threadInfo}",
            Color = new Color(EmbedTheme.Success)
        };

        await FollowupAsync(new InteractionMessageProperties { Embeds = [embed] });
    }

    [SlashCommand("news-setup", "Создать или настроить специальный канал #новости на сервере")]
    public async Task SetupNewsAsync(
        [SlashCommandParameter(Name = "channel_name", Description = "Имя канала (по умолчанию: новости)")] string channelName = "новости",
        [SlashCommandParameter(Name = "category", Description = "Категория (по умолчанию: 📰 ИНФО И НОВОСТИ)")] string category = "📰 ИНФО И НОВОСТИ")
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<ChannelId>>(new SetupNewsChannel(
            GuildId.From(Context.Guild!.Id),
            channelName,
            category));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось настроить канал новостей"));
            return;
        }

        var embed = new EmbedProperties
        {
            Title = "✅ Канал новостей готов!",
            Description = $"Канал <#{result.Value.Value}> настроен в разделе **`{category}`**.\nТеперь при вызове `/news` или `!news` свежие события будут публиковаться туда.",
            Color = new Color(EmbedTheme.Success)
        };

        await FollowupAsync(new InteractionMessageProperties { Embeds = [embed] });
    }
}
