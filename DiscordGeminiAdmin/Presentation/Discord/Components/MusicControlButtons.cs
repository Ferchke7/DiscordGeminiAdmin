using Lavalink4NET;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace DiscordGeminiAdmin.Presentation.Discord.Components;

/// <summary>
/// Интерактивные кнопки управления плеером прямо под сообщением с треком.
/// </summary>
public sealed class MusicControlButtons(IAudioService audioService)
    : ComponentInteractionModule<ButtonInteractionContext>
{
    private async ValueTask<bool> EnsureAccessAsync()
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

    [ComponentInteraction("music-toggle")]
    public async Task TogglePauseAsync()
    {
        if (!await EnsureAccessAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || player.CurrentTrack is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Сейчас ничего не играет.")));
            return;
        }

        if (player.IsPaused)
        {
            await player.ResumeAsync();
            await RespondAsync(InteractionCallback.Message(EmbedTheme.SuccessCard(
                "▶️ Воспроизведение возобновлено",
                $"Трек: **`{player.CurrentTrack.Title}`**")));
        }
        else
        {
            await player.PauseAsync();
            await RespondAsync(InteractionCallback.Message(EmbedTheme.Card(
                "⏸️ Воспроизведение приостановлено",
                $"Трек: **`{player.CurrentTrack.Title}`**",
                EmbedTheme.Warning)));
        }
    }

    [ComponentInteraction("music-skip")]
    public async Task SkipAsync()
    {
        if (!await EnsureAccessAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || player.CurrentTrack is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Сейчас ничего не играет.")));
            return;
        }

        var skipped = player.CurrentTrack.Title;
        await player.SkipAsync();

        var next = player.CurrentTrack?.Title ?? "Очередь пуста";
        await RespondAsync(InteractionCallback.Message(EmbedTheme.Card(
            "⏭️ Трек пропущен",
            $"Пропущен: **`{skipped}`**\nСейчас играет: **`{next}`**",
            EmbedTheme.Primary)));
    }

    [ComponentInteraction("music-shuffle")]
    public async Task ShuffleAsync()
    {
        if (!await EnsureAccessAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || player.Queue.Count == 0)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("В очереди недостаточно треков для перемешивания.")));
            return;
        }

        await player.Queue.ShuffleAsync();
        await RespondAsync(InteractionCallback.Message(EmbedTheme.SuccessCard(
            "🔀 Очередь перемешана",
            $"Перемешано треков: **`{player.Queue.Count}`**")));
    }

    [ComponentInteraction("music-queue")]
    public async Task QueueAsync()
    {
        if (!await EnsureAccessAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || (player.CurrentTrack is null && player.Queue.Count == 0))
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.Card("📜 Очередь пуста", "Добавьте музыку с помощью `/play`.", EmbedTheme.Secondary)));
            return;
        }

        var current = player.CurrentTrack;
        var queueItems = player.Queue.Take(10).ToList();

        var desc = current is not null
            ? $"**Сейчас играет:**\n🎶 **`{current.Title}`** ({current.Author}) — `{FormatDuration(current.Duration)}`\n\n**Далее в очереди ({player.Queue.Count}):**\n"
            : $"**В очереди ({player.Queue.Count}):**\n";

        if (queueItems.Count == 0)
        {
            desc += "_Очередь пуста._";
        }
        else
        {
            for (var i = 0; i < queueItems.Count; i++)
            {
                var track = queueItems[i].Track;
                var title = track?.Title ?? "Неизвестный трек";
                var dur = track is not null ? FormatDuration(track.Duration) : "--:--";
                desc += $"`{i + 1}.` **{title}** — `{dur}`\n";
            }
        }

        await RespondAsync(InteractionCallback.Message(EmbedTheme.Card("📜 Музыкальная очередь", desc, EmbedTheme.Primary)));
    }

    [ComponentInteraction("music-stop")]
    public async Task StopAsync()
    {
        if (!await EnsureAccessAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.Card("⏹️ Плеер не активен", "Бот не подключен к голосовому каналу.", EmbedTheme.Secondary)));
            return;
        }

        await player.StopAsync();
        await player.DisconnectAsync();

        await RespondAsync(InteractionCallback.Message(EmbedTheme.Card(
            "⏹️ Воспроизведение остановлено",
            "Очередь очищена, бот отключился от голосового канала.",
            EmbedTheme.Warning)));
    }

    private static string FormatDuration(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
}
