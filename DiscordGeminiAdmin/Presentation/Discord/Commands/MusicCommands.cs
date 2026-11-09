using System.Text;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using Lavalink4NET;
using Lavalink4NET.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

/// <summary>
/// Профессиональные команды музыкального плеера на базе Lavalink v4.
/// Воспроизведение треков, плейлистов с YouTube и Яндекс.Музыки, управление очередью и громкостью.
/// </summary>
public sealed class MusicCommands(IAudioService audioService) : ApplicationCommandModule<ApplicationCommandContext>
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

    [SlashCommand("play", "Включить музыку: трек или плейлист (YouTube, Яндекс.Музыка, SoundCloud, поиск)")]
    public async Task PlayAsync(
        [SlashCommandParameter(Name = "query", Description = "Ссылка на трек/плейлист (YouTube/Яндекс) или название для поиска")] string query)
    {
        if (!await EnsureModeratorAsync())
            return;

        if (!Context.Guild!.VoiceStates.TryGetValue(Context.User.Id, out var voiceState) || !voiceState.ChannelId.HasValue)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Подключитесь к любому голосовому каналу, чтобы включить музыку.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild.Id);
        if (player is null)
        {
            player = await audioService.Players.JoinAsync<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
                Context.Guild.Id,
                voiceState.ChannelId.Value,
                PlayerFactory.Queued);
        }

        var cleanQuery = MusicQueryHelper.Clean(query);
        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            await FollowupAsync(EmbedTheme.ErrorCard("Укажите поисковый запрос или ссылку на трек/плейлист."));
            return;
        }

        var isUrl = MusicQueryHelper.IsUrl(cleanQuery);
        var hasSearchPrefix = MusicQueryHelper.HasSearchPrefix(cleanQuery);
        var loadMode = (isUrl || hasSearchPrefix) ? TrackSearchMode.None : TrackSearchMode.YouTubeMusic;
        var searchResult = await audioService.Tracks.LoadTracksAsync(cleanQuery, loadMode);

        if ((!searchResult.HasMatches || searchResult.IsFailed) && !isUrl && !hasSearchPrefix)
        {
            searchResult = await audioService.Tracks.LoadTracksAsync(cleanQuery, TrackSearchMode.YouTube);
        }

        if ((!searchResult.HasMatches || searchResult.IsFailed) && !isUrl && !hasSearchPrefix)
        {
            searchResult = await audioService.Tracks.LoadTracksAsync(cleanQuery, TrackSearchMode.SoundCloud);
        }

        if (!searchResult.HasMatches || searchResult.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(
                searchResult.Exception?.Message ?? "Ничего не найдено",
                $"По запросу «{cleanQuery}» не удалось найти аудиозаписи."));
            return;
        }

        if (searchResult.IsPlaylist)
        {
            var playlistTracks = searchResult.Tracks;
            var firstTrack = playlistTracks[0];

            await player.PlayAsync(firstTrack);

            if (playlistTracks.Length > 1)
            {
                var remaining = playlistTracks.Skip(1).Select(t => (ITrackQueueItem)new TrackQueueItem(t));
                await player.Queue.InsertRangeAsync(player.Queue.Count, remaining);
            }

            var totalSeconds = playlistTracks.Sum(t => t.Duration.TotalSeconds);
            var totalDuration = TimeSpan.FromSeconds(totalSeconds);

            var playlistName = !string.IsNullOrWhiteSpace(searchResult.Playlist.Name)
                ? searchResult.Playlist.Name
                : "Плейлист";

            var embed = new EmbedProperties
            {
                Title = $"🎶 Добавлен плейлист: {playlistName}",
                Description = $"Загружено треков: **`{playlistTracks.Length}`**\nОбщая длительность: **`{FormatDuration(totalDuration)}`**\n\n**Сейчас играет:** `{firstTrack.Title}` ({firstTrack.Author})",
                Color = new Color(EmbedTheme.Purple),
                Thumbnail = firstTrack.ArtworkUri is not null ? new EmbedThumbnailProperties(firstTrack.ArtworkUri.ToString()) : null,
                Footer = new EmbedFooterProperties { Text = $"Запросил: @{Context.User.Username} • Lavalink v4" }
            };

            await FollowupAsync(new InteractionMessageProperties
            {
                Embeds = [embed],
                Components = [MusicButtonsRow]
            });
            return;
        }

        var singleTrack = searchResult.Track ?? searchResult.Tracks[0];
        var queuePosition = await player.PlayAsync(singleTrack);

        if (queuePosition == 0)
        {
            var embed = new EmbedProperties
            {
                Title = "▶️ Сейчас играет",
                Description = $"**[{singleTrack.Title}]({singleTrack.Uri})**\n\n👤 **Автор:** `{singleTrack.Author}`\n⏱️ **Длительность:** `{FormatDuration(singleTrack.Duration)}`",
                Color = new Color(EmbedTheme.Success),
                Thumbnail = singleTrack.ArtworkUri is not null ? new EmbedThumbnailProperties(singleTrack.ArtworkUri.ToString()) : null,
                Footer = new EmbedFooterProperties { Text = $"Запросил: @{Context.User.Username} • Lavalink v4" }
            };

            await FollowupAsync(new InteractionMessageProperties
            {
                Embeds = [embed],
                Components = [MusicButtonsRow]
            });
        }
        else
        {
            var embed = new EmbedProperties
            {
                Title = "📝 Трек добавлен в очередь",
                Description = $"**[{singleTrack.Title}]({singleTrack.Uri})**\n\n👤 **Автор:** `{singleTrack.Author}`\n⏱️ **Длительность:** `{FormatDuration(singleTrack.Duration)}`\n🔢 **Позиция в очереди:** `#{queuePosition}`",
                Color = new Color(EmbedTheme.Primary),
                Thumbnail = singleTrack.ArtworkUri is not null ? new EmbedThumbnailProperties(singleTrack.ArtworkUri.ToString()) : null,
                Footer = new EmbedFooterProperties { Text = $"Запросил: @{Context.User.Username}" }
            };

            await FollowupAsync(new InteractionMessageProperties
            {
                Embeds = [embed]
            });
        }
    }

    [SlashCommand("now", "Показать текущий играющий трек, автора и прогресс")]
    public async Task NowPlayingAsync()
    {
        if (!await EnsureModeratorAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || player.CurrentTrack is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Сейчас ничего не играет.")));
            return;
        }

        var track = player.CurrentTrack;
        var position = player.Position?.Position ?? TimeSpan.Zero;
        var total = track.Duration;
        var bar = BuildProgressBar(position, total);

        var statusIcon = player.IsPaused ? "⏸️" : "▶️";
        var repeatStr = player.RepeatMode switch
        {
            TrackRepeatMode.Track => "🔂 Трек",
            TrackRepeatMode.Queue => "🔁 Очередь",
            _ => "Выкл"
        };

        var embed = new EmbedProperties
        {
            Title = $"{statusIcon} Сейчас играет",
            Description = $"""
                **[{track.Title}]({track.Uri})**
                Автор: `{track.Author}`

                `{FormatDuration(position)}` {bar} `{FormatDuration(total)}`

                🔊 **Громкость:** `{Math.Round(player.Volume * 100)}%` • 🔁 **Повтор:** `{repeatStr}` • 📜 **В очереди:** `{player.Queue.Count}`
                """,
            Color = new Color(EmbedTheme.Primary),
            Thumbnail = track.ArtworkUri is not null ? new EmbedThumbnailProperties(track.ArtworkUri.ToString()) : null,
            Footer = new EmbedFooterProperties { Text = "Code-Guru Music Player • Lavalink v4" }
        };

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties
        {
            Embeds = [embed],
            Components = [MusicButtonsRow]
        }));
    }

    [SlashCommand("pause", "Поставить текущий трек на паузу")]
    public async Task PauseAsync()
    {
        if (!await EnsureModeratorAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || player.CurrentTrack is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Сейчас ничего не играет.")));
            return;
        }

        if (player.IsPaused)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.Card("⏸️ Уже на паузе", "Используйте `/resume` для возобновления.", EmbedTheme.Secondary)));
            return;
        }

        await player.PauseAsync();
        await RespondAsync(InteractionCallback.Message(EmbedTheme.Card("⏸️ Пауза", $"Воспроизведение приостановлено: **`{player.CurrentTrack.Title}`**", EmbedTheme.Warning)));
    }

    [SlashCommand("resume", "Возобновить воспроизведение музыки")]
    public async Task ResumeAsync()
    {
        if (!await EnsureModeratorAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || player.CurrentTrack is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Сейчас ничего не играет.")));
            return;
        }

        if (!player.IsPaused)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.Card("▶️ Уже играет", "Трек не стоит на паузе.", EmbedTheme.Secondary)));
            return;
        }

        await player.ResumeAsync();
        await RespondAsync(InteractionCallback.Message(EmbedTheme.SuccessCard("▶️ Возобновлено", $"Воспроизведение продолжено: **`{player.CurrentTrack.Title}`**")));
    }

    [SlashCommand("skip", "Пропустить текущий трек в очереди")]
    public async Task SkipAsync(
        [SlashCommandParameter(Name = "count", Description = "Сколько треков пропустить (по умолчанию: 1)")] int count = 1)
    {
        if (!await EnsureModeratorAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || player.CurrentTrack is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Сейчас ничего не играет.")));
            return;
        }

        var skippedTitle = player.CurrentTrack.Title;
        var clampedCount = Math.Max(1, count);

        await player.SkipAsync(clampedCount);

        var nextTrack = player.CurrentTrack;
        var desc = clampedCount == 1
            ? $"Пропущен трек: **`{skippedTitle}`**"
            : $"Пропущено треков: **`{clampedCount}`**";

        if (nextTrack is not null)
            desc += $"\n\n▶️ **Сейчас играет:** `{nextTrack.Title}` ({nextTrack.Author})";
        else
            desc += "\n\n_Очередь воспроизведения подошла к концу._";

        await RespondAsync(InteractionCallback.Message(EmbedTheme.SuccessCard("⏭️ Трек пропущен", desc)));
    }

    [SlashCommand("stop", "Остановить воспроизведение, очистить очередь и отключить бота")]
    public async Task StopAsync()
    {
        if (!await EnsureModeratorAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.Card("⏹️ Плеер не активен", "Бот не находится в голосовом канале.", EmbedTheme.Secondary)));
            return;
        }

        await player.StopAsync();
        await player.DisconnectAsync();

        await RespondAsync(InteractionCallback.Message(EmbedTheme.Card(
            "⏹️ Воспроизведение остановлено",
            "Очередь полностью очищена, бот покинул голосовой канал.",
            EmbedTheme.Warning)));
    }

    [SlashCommand("queue", "Показать список треков в очереди")]
    public async Task ShowQueueAsync(
        [SlashCommandParameter(Name = "page", Description = "Номер страницы очереди (по умолчанию: 1)")] int page = 1)
    {
        if (!await EnsureModeratorAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null || (player.CurrentTrack is null && player.Queue.Count == 0))
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.Card("📜 Очередь пуста", "Добавьте музыку с помощью `/play`.", EmbedTheme.Secondary)));
            return;
        }

        const int pageSize = 10;
        var totalTracks = player.Queue.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalTracks / (double)pageSize));
        var currentPage = Math.Clamp(page, 1, totalPages);
        var skip = (currentPage - 1) * pageSize;

        var current = player.CurrentTrack;
        var queueItems = player.Queue.Skip(skip).Take(pageSize).ToList();

        var sb = new StringBuilder();
        if (current is not null)
        {
            sb.AppendLine($"**Сейчас играет:**");
            sb.AppendLine($"🎶 **[{current.Title}]({current.Uri})** ({current.Author}) — `{FormatDuration(current.Duration)}`");
            sb.AppendLine();
        }

        sb.AppendLine($"**В очереди ({totalTracks} треков):**");
        if (queueItems.Count == 0)
        {
            sb.AppendLine("_Очередь пуста._");
        }
        else
        {
            for (var i = 0; i < queueItems.Count; i++)
            {
                var track = queueItems[i].Track;
                var title = track?.Title ?? "Неизвестный трек";
                var dur = track is not null ? FormatDuration(track.Duration) : "--:--";
                sb.AppendLine($"`{skip + i + 1}.` **{title}** — `{dur}`");
            }
        }

        var embed = new EmbedProperties
        {
            Title = $"📜 Музыкальная очередь (Стр. {currentPage}/{totalPages})",
            Description = sb.ToString(),
            Color = new Color(EmbedTheme.Primary),
            Footer = new EmbedFooterProperties { Text = $"Всего в очереди: {totalTracks} • Повтор: {player.RepeatMode}" }
        };

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties
        {
            Embeds = [embed],
            Components = [MusicButtonsRow]
        }));
    }

    [SlashCommand("shuffle", "Перемешать треки в очереди в случайном порядке")]
    public async Task ShuffleAsync()
    {
        if (!await EnsureModeratorAsync())
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
            $"Успешно перемешано треков: **`{player.Queue.Count}`**")));
    }

    [SlashCommand("repeat", "Режим зацикливания воспроизведения (выкл, трек, очередь)")]
    public async Task RepeatAsync(
        [SlashCommandParameter(Name = "mode", Description = "Режим повтора: off, track, queue")] string mode)
    {
        if (!await EnsureModeratorAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Плеер не запущен.")));
            return;
        }

        var targetMode = mode.ToLowerInvariant() switch
        {
            "track" or "трек" or "1" => TrackRepeatMode.Track,
            "queue" or "очередь" or "all" => TrackRepeatMode.Queue,
            _ => TrackRepeatMode.None
        };

        player.RepeatMode = targetMode;

        var modeLabel = targetMode switch
        {
            TrackRepeatMode.Track => "🔂 Зацикливание текущего трека",
            TrackRepeatMode.Queue => "🔁 Зацикливание всей очереди",
            _ => "❌ Зацикливание выключено"
        };

        await RespondAsync(InteractionCallback.Message(EmbedTheme.SuccessCard("🔁 Режим повтора изменён", modeLabel)));
    }

    [SlashCommand("volume", "Установить громкость музыки (от 1 до 100)")]
    public async Task VolumeAsync(
        [SlashCommandParameter(Name = "level", Description = "Уровень громкости от 1 до 100")] int level)
    {
        if (!await EnsureModeratorAsync())
            return;

        var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(Context.Guild!.Id);
        if (player is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Плеер не запущен.")));
            return;
        }

        var clamped = Math.Clamp(level, 1, 100);
        await player.SetVolumeAsync(clamped / 100f);

        await RespondAsync(InteractionCallback.Message(EmbedTheme.SuccessCard(
            "🔊 Громкость изменена",
            $"Установлена громкость: **`{clamped}%`**")));
    }

    private static readonly ActionRowProperties MusicButtonsRow = new([
        new ButtonProperties("music-toggle", "⏯️", ButtonStyle.Primary),
        new ButtonProperties("music-skip", "⏭️", ButtonStyle.Secondary),
        new ButtonProperties("music-shuffle", "🔀", ButtonStyle.Secondary),
        new ButtonProperties("music-queue", "📜", ButtonStyle.Secondary),
        new ButtonProperties("music-stop", "⏹️", ButtonStyle.Danger)
    ]);

    private static string FormatDuration(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");

    private static string BuildProgressBar(TimeSpan current, TimeSpan total, int length = 12)
    {
        if (total <= TimeSpan.Zero) return "🔘" + new string('▬', length);
        var progress = Math.Clamp(current.TotalSeconds / total.TotalSeconds, 0.0, 1.0);
        var point = (int)Math.Round(progress * length);
        var sb = new StringBuilder();
        for (var i = 0; i <= length; i++)
        {
            sb.Append(i == point ? "🔘" : "▬");
        }
        return sb.ToString();
    }
}
