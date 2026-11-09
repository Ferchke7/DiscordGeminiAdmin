using System.Diagnostics;
using System.Text.RegularExpressions;
using DiscordGeminiAdmin.Application.UseCases.Moderation.AdminActions;
using DiscordGeminiAdmin.Application.UseCases.Moderation.IngestMessage;
using DiscordGeminiAdmin.Application.UseCases.Structure.Analyze;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Presentation.Discord.Commands;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using Lavalink4NET;
using Lavalink4NET.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Events;

/// <summary>
/// Обрабатывает входящие сообщения в каналах:
/// 1) Мгновенно выполняет просьбы к боту (@Code-Guru ..., /setup ..., /ai ..., /help, /server, !play, !stop).
/// 2) Буферизует обычные сообщения участников для последующего часового ИИ-анализа.
/// </summary>
public sealed partial class MessageCreateGatewayHandler(
    IServiceScopeFactory scopeFactory,
    RestClient restClient,
    IAudioService audioService,
    ILogger<MessageCreateGatewayHandler> logger) : IMessageCreateGatewayHandler
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    [GeneratedRegex(@"^<@!?\d+>\s*", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingBotMentionRegex();

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

        var raw = message.Content.Trim();
        try
        {
            if (await TryHandleTextCommandOrMentionAsync(message, raw, guildId, channelId, bus))
                return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка обработки текстовой команды в канале {ChannelId}", message.ChannelId);
            try
            {
                var errCard = EmbedTheme.ExceptionCard(ex, "Ошибка выполнения текстовой команды");
                await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(errCard));
            }
            catch (Exception sendEx)
            {
                logger.LogWarning(sendEx, "Не удалось отправить отчёт об ошибке в канал {ChannelId}", message.ChannelId);
            }
            return;
        }

        await bus.InvokeAsync<Result>(new IngestMessage(
            guildId,
            channelId,
            authorId,
            discordMessageId,
            message.Author.Username,
            message.Content));
    }

    private async ValueTask<bool> TryHandleTextCommandOrMentionAsync(
        Message message,
        string raw,
        GuildId guildId,
        ChannelId channelId,
        IMessageBus bus)
    {
        var lower = raw.ToLowerInvariant();
        var isCommandOrMention =
            lower is "/help" or "!help" or "!помощь" or "/помощь" or "/server" or "!server" or "/status" or "!status" or "/info" or "!info"
            || lower is "!stop" or "/stop" or "!skip" or "/skip" or "!now" or "/now" or "!queue" or "/queue"
            || lower.StartsWith("!play", StringComparison.Ordinal)
            || lower.StartsWith("/play", StringComparison.Ordinal)
            || lower.StartsWith("/help ", StringComparison.Ordinal)
            || lower.StartsWith("!help ", StringComparison.Ordinal)
            || lower.StartsWith("/setup", StringComparison.Ordinal)
            || lower.StartsWith("!setup", StringComparison.Ordinal)
            || lower.StartsWith("/reset-setup", StringComparison.Ordinal)
            || lower.StartsWith("!reset-setup", StringComparison.Ordinal)
            || lower.StartsWith("/mod-config set-theme", StringComparison.Ordinal)
            || lower.StartsWith("/ai", StringComparison.Ordinal)
            || lower.StartsWith("!ai", StringComparison.Ordinal)
            || lower.StartsWith("/code", StringComparison.Ordinal)
            || lower.StartsWith("!code", StringComparison.Ordinal)
            || lower.StartsWith("/ask", StringComparison.Ordinal)
            || lower.StartsWith("!ask", StringComparison.Ordinal)
            || lower.StartsWith("/history", StringComparison.Ordinal)
            || lower.StartsWith("!history", StringComparison.Ordinal)
            || lower.StartsWith("/organize", StringComparison.Ordinal)
            || lower.StartsWith("!organize", StringComparison.Ordinal)
            || LeadingBotMentionRegex().IsMatch(raw);

        if (!isCommandOrMention)
            return false;

        if (!await ModeratorGuard.IsModeratorAsync(message.Author, null, message.GuildId, restClient))
        {
            await restClient.SendMessageAsync(
                message.ChannelId,
                EmbedTheme.ToChannelMessage(ModeratorGuard.AccessDeniedCard()));
            return true;
        }

        // 0. Музыкальные текстовые команды (!play, !stop, !skip, !now, !queue)
        if (lower.StartsWith("!play", StringComparison.Ordinal) || lower.StartsWith("/play ", StringComparison.Ordinal))
        {
            var query = raw.Contains(' ') ? raw[(raw.IndexOf(' ') + 1)..].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(
                    EmbedTheme.ErrorCard("Укажите ссылку на трек, плейлист или поисковый запрос (например: `!play eminem`)")));
                return true;
            }

            var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(guildId.Value);
            var isUrl = query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        query.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            var loadMode = isUrl ? TrackSearchMode.None : TrackSearchMode.YouTube;
            var searchResult = await audioService.Tracks.LoadTracksAsync(query, loadMode);

            if (!searchResult.HasMatches || searchResult.IsFailed)
            {
                await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(
                    EmbedTheme.ErrorCard($"По запросу «{query}» ничего не найдено.")));
                return true;
            }

            if (player is null)
            {
                await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(
                    EmbedTheme.ErrorCard("Используйте слэш-команду `/play`, чтобы бот определил ваш голосовой канал и подключился к нему.")));
                return true;
            }

            if (searchResult.IsPlaylist)
            {
                var playlistTracks = searchResult.Tracks;
                await player.PlayAsync(playlistTracks[0]);
                if (playlistTracks.Length > 1)
                {
                    var remaining = playlistTracks.Skip(1).Select(t => (ITrackQueueItem)new TrackQueueItem(t));
                    await player.Queue.InsertRangeAsync(player.Queue.Count, remaining);
                }

                var totalDuration = TimeSpan.FromSeconds(playlistTracks.Sum(t => t.Duration.TotalSeconds));
                var card = EmbedTheme.Card(
                    $"🎶 Добавлен плейлист: {searchResult.Playlist.Name}",
                    $"Загружено треков: **`{playlistTracks.Length}`**\nОбщая длительность: **`{totalDuration:hh\\:mm\\:ss}`**",
                    EmbedTheme.Purple);
                await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
                return true;
            }

            var track = searchResult.Track ?? searchResult.Tracks[0];
            var pos = await player.PlayAsync(track);
            var resultCard = pos == 0
                ? EmbedTheme.SuccessCard("▶️ Сейчас играет", $"**`{track.Title}`** ({track.Author})")
                : EmbedTheme.Card("📝 Добавлено в очередь", $"**`{track.Title}`** ({track.Author}) — позиция #{pos}", EmbedTheme.Primary);

            await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(resultCard));
            return true;
        }

        if (lower is "!stop" or "/stop")
        {
            var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(guildId.Value);
            if (player is not null)
            {
                await player.StopAsync();
                await player.DisconnectAsync();
                await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(
                    EmbedTheme.Card("⏹️ Плеер остановлен", "Очередь очищена, бот отключился от канала.", EmbedTheme.Warning)));
            }
            return true;
        }

        if (lower is "!skip" or "/skip")
        {
            var player = await audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(guildId.Value);
            if (player is not null && player.CurrentTrack is not null)
            {
                var skipped = player.CurrentTrack.Title;
                await player.SkipAsync();
                await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(
                    EmbedTheme.SuccessCard("⏭️ Трек пропущен", $"Пропущен: **`{skipped}`**")));
            }
            return true;
        }

        // 1. /help, !help, !помощь
        if (lower is "/help" or "!help" or "!помощь" or "/помощь"
            || lower.StartsWith("/help ", StringComparison.Ordinal)
            || lower.StartsWith("!help ", StringComparison.Ordinal))
        {
            var card = HelpSectionBuilder.BuildMessage("overview", ephemeral: false);
            await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
            return true;
        }

        // 2. /server, /status, !status, /info
        if (lower is "/server" or "!server" or "/status" or "!status" or "/info" or "!info")
        {
            await SendStatusCardAsync(message.ChannelId, guildId, bus);
            return true;
        }

        // 3. /reset-setup ... или !reset-setup ... — полная очистка и пересборка сервера с описаниями
        if (lower.StartsWith("/reset-setup", StringComparison.Ordinal)
            || lower.StartsWith("!reset-setup", StringComparison.Ordinal))
        {
            var theme = raw
                .Replace("/reset-setup", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("!reset-setup", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("theme:", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim(' ', '"', '\'');
            var effectiveTheme = string.IsNullOrWhiteSpace(theme) ? null : theme;

            var goal = effectiveTheme is not null
                ? $"Полностью пересобери сервер с нуля под тематику «{effectiveTheme}»: создай категории, текстовые каналы (обязательно с подробными правилами и описаниями для каждого канала), голосовые комнаты и роли."
                : "Полностью пересобери сервер с нуля под его тематику: создай категории, текстовые каналы (обязательно с подробными правилами и описаниями для каждого канала), голосовые комнаты и роли.";

            var result = await bus.InvokeAsync<Result<StructureProposal>>(
                new AnalyzeGuildStructure(guildId, goal, channelId, ApplyImmediately: true, NewTheme: effectiveTheme, ResetExisting: true));

            var card = result.IsSuccess
                ? OrganizeCommands.BuildAppliedStructureCard($"🔄 Сервер полностью очищен и пересобран{(effectiveTheme is not null ? $": {effectiveTheme}" : string.Empty)}", result.Value)
                : EmbedTheme.ErrorCard(result, "Не удалось выполнить сброс и пересборку сервера");

            await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
            return true;
        }

        // 4. /setup ... или !setup ... — сразу задаёт тематику и поднимает группы/голосовые/текстовые каналы и роли
        if (lower.StartsWith("/setup", StringComparison.Ordinal)
            || lower.StartsWith("!setup", StringComparison.Ordinal)
            || lower.StartsWith("/mod-config set-theme", StringComparison.Ordinal))
        {
            var theme = raw
                .Replace("/mod-config set-theme", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/setup", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("!setup", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("theme:", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim(' ', '"', '\'');
            if (string.IsNullOrWhiteSpace(theme))
                theme = "IT и разработка";

            var goal = $"Подними и организуй сервер под тематику «{theme}»: создай удобные группы (категории), текстовые каналы, голосовые комнаты для общения/созвонов и базовые роли.";
            var result = await bus.InvokeAsync<Result<StructureProposal>>(
                new AnalyzeGuildStructure(guildId, goal, channelId, ApplyImmediately: true, NewTheme: theme));

            var card = result.IsSuccess
                ? OrganizeCommands.BuildAppliedStructureCard($"🚀 Сервер настроен под тематику: {theme}", result.Value)
                : EmbedTheme.ErrorCard(result, "Не удалось автоматически поднять структуру сервера");

            await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
            return true;
        }

        // 4. /ai ..., /code ..., /ask ..., /history ..., /organize ...
        if (lower.StartsWith("/ai", StringComparison.Ordinal)
            || lower.StartsWith("!ai", StringComparison.Ordinal)
            || lower.StartsWith("/code", StringComparison.Ordinal)
            || lower.StartsWith("!code", StringComparison.Ordinal)
            || lower.StartsWith("/ask", StringComparison.Ordinal)
            || lower.StartsWith("!ask", StringComparison.Ordinal)
            || lower.StartsWith("/history", StringComparison.Ordinal)
            || lower.StartsWith("!history", StringComparison.Ordinal)
            || lower.StartsWith("/organize", StringComparison.Ordinal)
            || lower.StartsWith("!organize", StringComparison.Ordinal))
        {
            var prompt = raw.Contains(' ') ? raw[(raw.IndexOf(' ') + 1)..].Trim() : "Сделай краткую сводку последних сообщений в чате";
            var result = await bus.InvokeAsync<Result<StructureProposal>>(
                new AnalyzeGuildStructure(guildId, prompt, channelId, ApplyImmediately: true));

            var card = result.IsSuccess
                ? OrganizeCommands.BuildAppliedStructureCard(
                    result.Value.Changes.Count > 0 ? "🤖 ИИ выполнил организацию сервера" : "🤖 Ответ Code-Guru AI",
                    result.Value)
                : EmbedTheme.ErrorCard(result, "Ошибка выполнения запроса ИИ");

            await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
            return true;
        }

        // 5. Упоминание бота (@Code-Guru <запрос>) — пишет код, отвечает на вопросы, ищет в истории чата или организует сервер
        if (LeadingBotMentionRegex().IsMatch(raw))
        {
            var afterMention = LeadingBotMentionRegex().Replace(raw, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(afterMention)
                || afterMention.Equals("help", StringComparison.OrdinalIgnoreCase)
                || afterMention.Equals("помощь", StringComparison.OrdinalIgnoreCase)
                || afterMention.Equals("команды", StringComparison.OrdinalIgnoreCase)
                || afterMention.Equals("привет", StringComparison.OrdinalIgnoreCase))
            {
                var helpCard = HelpSectionBuilder.BuildMessage("overview", ephemeral: false);
                await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(helpCard));
                return true;
            }

            var orgResult = await bus.InvokeAsync<Result<StructureProposal>>(
                new AnalyzeGuildStructure(guildId, afterMention, channelId, ApplyImmediately: true));

            var card = orgResult.IsSuccess
                ? OrganizeCommands.BuildAppliedStructureCard(
                    orgResult.Value.Changes.Count > 0 ? "🤖 ИИ выполнил организацию сервера" : "🤖 Ответ Code-Guru AI",
                    orgResult.Value)
                : EmbedTheme.ErrorCard(orgResult, "Ошибка выполнения запроса ИИ");

            await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
            return true;
        }

        return false;
    }

    private async ValueTask SendStatusCardAsync(ulong rawChannelId, GuildId guildId, IMessageBus bus)
    {
        var dashboardRes = await bus.InvokeAsync<Result<GuildDashboardReport>>(new GetGuildDashboard(guildId));
        var uptime = DateTime.UtcNow - StartedAtUtc;
        var memMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        var profile = dashboardRes.IsSuccess ? dashboardRes.Value.Profile : null;
        var structure = dashboardRes.IsSuccess ? dashboardRes.Value.Structure : null;

        var card = EmbedTheme.Card(
            $"📊 Статус сервера: {structure?.GuildName ?? "Discord"}",
            $"**Тематика сервера:** `{profile?.Theme.Value ?? "не задана (/setup)"}`",
            EmbedTheme.Success,
            [
                new EmbedFieldProperties
                {
                    Name = "🏗️ Структура",
                    Value = $"• Групп: `{structure?.Categories.Count ?? 0}`\n• Голосовых: `{structure?.Channels.Count(c => c.IsVoice) ?? 0}`\n• Текстовых: `{structure?.Channels.Count(c => !c.IsVoice) ?? 0}`\n• Ролей: `{structure?.Roles.Count ?? 0}`",
                    Inline = true
                },
                new EmbedFieldProperties
                {
                    Name = "⚙️ Движок",
                    Value = $"• Аптайм: `{(int)uptime.TotalHours}ч {uptime.Minutes}м`\n• RAM: `{memMb} MB`\n• Модель: `gemini-3.8-flash`",
                    Inline = true
                }
            ],
            ephemeral: false);

        await restClient.SendMessageAsync(rawChannelId, EmbedTheme.ToChannelMessage(card));
    }
}
