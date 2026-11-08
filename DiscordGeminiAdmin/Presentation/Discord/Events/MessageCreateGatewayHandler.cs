using System.Diagnostics;
using System.Text.RegularExpressions;
using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.UseCases.Guilds.Setup;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.Ask;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.IndexDocument;
using DiscordGeminiAdmin.Application.UseCases.Moderation.AdminActions;
using DiscordGeminiAdmin.Application.UseCases.Moderation.IngestMessage;
using DiscordGeminiAdmin.Application.UseCases.Structure.Analyze;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Presentation.Discord.Commands;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Events;

/// <summary>
/// Обрабатывает входящие сообщения в каналах:
/// 1) Мгновенно отвечает на текстовые команды (/help, !help, /mod-config, /knowledge, /ask, /status) и упоминания @бота.
/// 2) Буферизует обычные сообщения участников для последующего часового ИИ-анализа.
/// </summary>
public sealed partial class MessageCreateGatewayHandler(
    IServiceScopeFactory scopeFactory,
    RestClient restClient,
    ILogger<MessageCreateGatewayHandler> logger) : IMessageCreateGatewayHandler
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    [GeneratedRegex(@"theme\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThemeQuotedRegex();

    [GeneratedRegex(@"description\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionQuotedRegex();

    [GeneratedRegex(@"query\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QueryQuotedRegex();

    [GeneratedRegex(@"<#(\d+)>", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelMentionRegex();

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

        // 1. /help, !help, !помощь
        if (lower is "/help" or "!help" or "!помощь" or "/помощь"
            || lower.StartsWith("/help ", StringComparison.Ordinal)
            || lower.StartsWith("!help ", StringComparison.Ordinal))
        {
            var parts = raw.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var section = parts.Length > 1 ? parts[1] : "overview";
            var card = HelpSectionBuilder.BuildMessage(section, ephemeral: false);
            await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
            return true;
        }

        // 2. /status, !status, /info, !info
        if (lower is "/status" or "!status" or "/info" or "!info" or "/info status")
        {
            await SendStatusCardAsync(message.ChannelId, guildId, bus);
            return true;
        }

        // 3. /mod-config set-theme ... или /mod setup ...
        if (lower.StartsWith("/mod-config set-theme", StringComparison.Ordinal)
            || lower.StartsWith("!set-theme", StringComparison.Ordinal)
            || lower.StartsWith("/mod setup", StringComparison.Ordinal))
        {
            await HandleSetThemeTextAsync(message.ChannelId, raw, guildId, channelId, bus);
            return true;
        }

        // 4. /mod-config set-channels ...
        if (lower.StartsWith("/mod-config set-channels", StringComparison.Ordinal)
            || lower.StartsWith("!set-channels", StringComparison.Ordinal))
        {
            await HandleSetChannelsTextAsync(message, raw, guildId, channelId, bus);
            return true;
        }

        // 5. /knowledge search ... или /ask ... или !ask ...
        if (lower.StartsWith("/knowledge search", StringComparison.Ordinal)
            || lower.StartsWith("/ask ", StringComparison.Ordinal)
            || lower.StartsWith("!ask ", StringComparison.Ordinal))
        {
            await HandleKnowledgeSearchTextAsync(message.ChannelId, raw, guildId, bus);
            return true;
        }

        // 6. /organize ... или !organize ...
        if (lower.StartsWith("/organize", StringComparison.Ordinal)
            || lower.StartsWith("!organize", StringComparison.Ordinal))
        {
            var goal = raw.Contains(' ') ? raw[(raw.IndexOf(' ') + 1)..].Trim() : null;
            if (string.Equals(goal, "analyze", StringComparison.OrdinalIgnoreCase))
                goal = null;
            else if (goal is not null && goal.StartsWith("analyze ", StringComparison.OrdinalIgnoreCase))
                goal = goal["analyze ".Length..].Trim();

            var result = await bus.InvokeAsync<Result<StructureProposal>>(
                new AnalyzeGuildStructure(guildId, goal, channelId));

            var card = result.IsSuccess
                ? EmbedTheme.SuccessCard(
                    "План реорганизации сформирован ИИ",
                    $"План улучшений (`{result.Value.Changes.Count}` шагов) опубликован выше. Нажмите **`[✅ Одобрить и применить]`**, чтобы применить структуру.",
                    ephemeral: false)
                : EmbedTheme.ErrorCard(result, "Ошибка анализа структуры");

            await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
            return true;
        }

        // 7. Упоминание бота (@Code-Guru ...)
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

            await AnswerQuestionInChannelAsync(message.ChannelId, guildId, afterMention, bus);
            return true;
        }

        return false;
    }

    private async ValueTask HandleSetThemeTextAsync(
        ulong rawChannelId,
        string raw,
        GuildId guildId,
        ChannelId fallbackChannelId,
        IMessageBus bus)
    {
        var themeMatch = ThemeQuotedRegex().Match(raw);
        var descMatch = DescriptionQuotedRegex().Match(raw);

        string theme;
        string? description = null;

        if (themeMatch.Success)
        {
            theme = themeMatch.Groups[1].Value.Trim();
            if (descMatch.Success)
                description = descMatch.Groups[1].Value.Trim();
        }
        else
        {
            var payload = raw
                .Replace("/mod-config set-theme", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("/mod setup", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("!set-theme", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
            theme = string.IsNullOrWhiteSpace(payload) ? "IT и разработка" : payload;
        }

        var combined = string.IsNullOrWhiteSpace(description) ? theme : $"{theme} — {description}";
        var res = await bus.InvokeAsync<Result<ChannelId>>(new SetGuildTheme(guildId, combined, fallbackChannelId));
        if (res.IsFailed)
        {
            await restClient.SendMessageAsync(rawChannelId, EmbedTheme.ToChannelMessage(EmbedTheme.ErrorCard(res)));
            return;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            await bus.InvokeAsync<Result<IndexDocumentResult>>(new IndexDocument(
                guildId,
                "theme#overview",
                KnowledgeSourceType.ThemeAndFaq,
                $"Тематика и концепция сервера: {theme}",
                description));
        }

        var card = EmbedTheme.SuccessCard(
            "Тематика и описание сообщества сохранены",
            "Настройки ИИ-модератора и RAG-базы обновлены. Вызовите `/organize analyze`, чтобы ИИ предложил структуру категорий и каналов под вашу концепцию!",
            [
                new EmbedFieldProperties { Name = "🎯 Тематика", Value = theme, Inline = true },
                new EmbedFieldProperties { Name = "📢 Мод-канал", Value = $"<#{res.Value.Value}>", Inline = true },
                new EmbedFieldProperties { Name = "📝 Концепция (RAG)", Value = description ?? "`не указана`", Inline = false }
            ],
            ephemeral: false);

        await restClient.SendMessageAsync(rawChannelId, EmbedTheme.ToChannelMessage(card));
    }

    private async ValueTask HandleSetChannelsTextAsync(
        Message message,
        string raw,
        GuildId guildId,
        ChannelId currentChannelId,
        IMessageBus bus)
    {
        var targetChannel = currentChannelId;
        var mentionMatch = ChannelMentionRegex().Match(raw);
        if (mentionMatch.Success && ulong.TryParse(mentionMatch.Groups[1].Value, out var mentionedId) && ChannelId.TryFrom(mentionedId, out var parsedMention))
        {
            targetChannel = parsedMention;
        }
        else if (message.GuildId is { } gId)
        {
            var channels = await restClient.GetGuildChannelsAsync(gId);
            var matched = channels.FirstOrDefault(c => c is TextGuildChannel && raw.Contains($"#{c.Name}", StringComparison.OrdinalIgnoreCase));
            if (matched is not null && ChannelId.TryFrom(matched.Id, out var byName))
                targetChannel = byName;
        }

        var res = await bus.InvokeAsync<Result<string>>(new SetGuildChannels(guildId, targetChannel));
        var card = res.IsSuccess
            ? EmbedTheme.SuccessCard(
                "Канал модерации обновлён",
                $"Все дайджесты ИИ, карточки авто-банов и планы структуры теперь направляются в <#{targetChannel.Value}>.",
                [
                    new EmbedFieldProperties { Name = "🛡️ Мод-канал", Value = $"<#{targetChannel.Value}>", Inline = true },
                    new EmbedFieldProperties { Name = "🎯 Тематика", Value = res.Value, Inline = true }
                ],
                ephemeral: false)
            : EmbedTheme.ErrorCard(res);

        await restClient.SendMessageAsync(message.ChannelId, EmbedTheme.ToChannelMessage(card));
    }

    private async ValueTask HandleKnowledgeSearchTextAsync(
        ulong rawChannelId,
        string raw,
        GuildId guildId,
        IMessageBus bus)
    {
        var queryMatch = QueryQuotedRegex().Match(raw);
        var question = queryMatch.Success
            ? queryMatch.Groups[1].Value.Trim()
            : raw.Replace("/knowledge search", string.Empty, StringComparison.OrdinalIgnoreCase)
                 .Replace("/ask", string.Empty, StringComparison.OrdinalIgnoreCase)
                 .Replace("!ask", string.Empty, StringComparison.OrdinalIgnoreCase)
                 .Replace("query:", string.Empty, StringComparison.OrdinalIgnoreCase)
                 .Trim(' ', '"', '\'');

        if (string.IsNullOrWhiteSpace(question))
        {
            await restClient.SendMessageAsync(
                rawChannelId,
                EmbedTheme.ToChannelMessage(EmbedTheme.ErrorCard("Укажите текст вопроса, например: `/ask можно ли кидать ссылки?`")));
            return;
        }

        await AnswerQuestionInChannelAsync(rawChannelId, guildId, question, bus);
    }

    private async ValueTask AnswerQuestionInChannelAsync(
        ulong rawChannelId,
        GuildId guildId,
        string question,
        IMessageBus bus)
    {
        var result = await bus.InvokeAsync<Result<KnowledgeAnswerDto>>(new AskKnowledge(guildId, question));
        if (result.IsFailed)
        {
            await restClient.SendMessageAsync(rawChannelId, EmbedTheme.ToChannelMessage(EmbedTheme.ErrorCard(result)));
            return;
        }

        var sources = result.Value.CitedRuleCodes.Count > 0
            ? string.Join(", ", result.Value.CitedRuleCodes.Select(c => $"`{c}`"))
            : "`общий контекст сервера`";

        var card = EmbedTheme.Card(
            "🧠 Ответ ИИ по базе знаний сервера (RAG)",
            result.Value.Answer,
            EmbedTheme.Purple,
            [
                new EmbedFieldProperties { Name = "❓ Вопрос", Value = question, Inline = false },
                new EmbedFieldProperties { Name = "📜 Источники", Value = sources, Inline = false }
            ],
            ephemeral: false);

        await restClient.SendMessageAsync(rawChannelId, EmbedTheme.ToChannelMessage(card));
    }

    private async ValueTask SendStatusCardAsync(ulong rawChannelId, GuildId guildId, IMessageBus bus)
    {
        var dashboardRes = await bus.InvokeAsync<Result<GuildDashboardReport>>(new GetGuildDashboard(guildId));
        var uptime = DateTime.UtcNow - StartedAtUtc;
        var memMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        var profile = dashboardRes.IsSuccess ? dashboardRes.Value.Profile : null;
        var rulesCount = dashboardRes.IsSuccess ? dashboardRes.Value.RulesCount : 0;

        var card = EmbedTheme.Card(
            "🟢 Статус системы Gemini AI Moderator",
            $"**Тематика сервера:** `{profile?.Theme.Value ?? "не задана (/mod setup)"}`",
            EmbedTheme.Success,
            [
                new EmbedFieldProperties
                {
                    Name = "⚙️ Движок",
                    Value = $"• Аптайм: `{(int)uptime.TotalHours}ч {uptime.Minutes}м`\n• RAM: `{memMb} MB`\n• Стек: `.NET 10 • NetCord`",
                    Inline = true
                },
                new EmbedFieldProperties
                {
                    Name = "🧠 Gemini & RAG",
                    Value = $"• Модератор: `gemini-2.5-flash`\n• Архитектор: `gemini-2.5-pro`\n• Документов в RAG: `{rulesCount}`",
                    Inline = true
                }
            ],
            ephemeral: false);

        await restClient.SendMessageAsync(rawChannelId, EmbedTheme.ToChannelMessage(card));
    }
}
