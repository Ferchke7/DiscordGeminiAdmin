using System.Diagnostics;
using System.Text;
using DiscordGeminiAdmin.Application.UseCases.Moderation.AdminActions;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

[SlashCommand("info", "Дашборд сервера, статус ИИ-бота, досье участников и журнал кейсов")]
public sealed class InfoCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    [SubSlashCommand("status", "Проверить состояние ИИ-бота, аптайм и настройки безопасности сервера")]
    public async Task StatusAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var dashboardRes = await bus.InvokeAsync<Result<GuildDashboardReport>>(
            new GetGuildDashboard(GuildId.From(Context.Guild.Id)));

        var uptime = DateTime.UtcNow - StartedAtUtc;
        var memMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        var profile = dashboardRes.IsSuccess ? dashboardRes.Value.Profile : null;
        var rulesCount = dashboardRes.IsSuccess ? dashboardRes.Value.RulesCount : 0;

        var policyText = profile is not null
            ? $"""
              • **Канал отчётов:** <#{profile.ModChannelId.Value}>
              • **Режим Dry-Run:** `{(profile.ModerationPolicy.DryRun ? "ВКЛ (тестовый)" : "ВЫКЛ (боевой авто-бан)")}`
              • **Мин. уверенность:** `{profile.ModerationPolicy.MinConfidence:P0}`
              • **Мин. доказательств:** `{profile.ModerationPolicy.MinEvidenceMessages}` сообщ.
              • **Лимит авто-банов/час:** `{profile.ModerationPolicy.MaxAutoBansPerHour}`
              """
            : "⚠️ Сервер ещё не настроен — выполните `/mod setup`";

        await FollowupAsync(EmbedTheme.Card(
            "🟢 Статус системы Gemini AI Moderator",
            $"**Тематика сервера:** `{profile?.Theme.Value ?? "не задана"}`",
            EmbedTheme.Success,
            [
                new EmbedFieldProperties
                {
                    Name = "⚙️ Движок и Ресурсы",
                    Value = $"""
                        • **Аптайм:** `{(int)uptime.TotalHours}ч {uptime.Minutes}м`
                        • **Память (RAM):** `{memMb} MB`
                        • **Платформа:** `.NET 10 • NetCord • Wolverine`
                        """,
                    Inline = true
                },
                new EmbedFieldProperties
                {
                    Name = "🧠 Модели Gemini & RAG",
                    Value = $"""
                        • **Модерация:** `gemini-2.5-flash`
                        • **Архитектор:** `gemini-2.5-pro`
                        • **RAG Векторы:** `pgvector (768d)`
                        • **Документов в RAG:** `{rulesCount}`
                        """,
                    Inline = true
                },
                new EmbedFieldProperties
                {
                    Name = "🛡️ Политика авто-модерации (`ModerationPolicy`)",
                    Value = policyText,
                    Inline = false
                }
            ]));
    }

    [SubSlashCommand("server", "Полная аналитика структуры Discord-сервера и сводка модерации")]
    public async Task ServerAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<GuildDashboardReport>>(
            new GetGuildDashboard(GuildId.From(Context.Guild.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        var d = result.Value;
        var textChannels = d.Structure?.Channels.Count(c => !c.IsVoice) ?? 0;
        var voiceChannels = d.Structure?.Channels.Count(c => c.IsVoice) ?? 0;
        var categories = d.Structure?.Categories.Count ?? 0;
        var roles = d.Structure?.Roles.Count ?? 0;

        await FollowupAsync(EmbedTheme.Card(
            $"📊 Аналитика сервера: {d.Structure?.GuildName ?? Context.Guild.Name}",
            $"**Тематика:** `{d.Profile?.Theme.Value ?? "Не настроена (/mod setup)"}`",
            EmbedTheme.Primary,
            [
                new EmbedFieldProperties
                {
                    Name = "🏗️ Структура каналов",
                    Value = $"""
                        • **Категорий:** `{categories}`
                        • **Текстовых каналов:** `{textChannels}`
                        • **Голосовых каналов:** `{voiceChannels}`
                        • **Ролей:** `{roles}`
                        """,
                    Inline = true
                },
                new EmbedFieldProperties
                {
                    Name = "🛡️ База знаний и Кейсы",
                    Value = $"""
                        • **Правил/чанков в RAG:** `{d.RulesCount}`
                        • **Недавних кейсов:** `{d.RecentCases.Count}`
                        • **Мод-канал:** {(d.Profile is not null ? $"<#{d.Profile.ModChannelId.Value}>" : "`не выбран`")}
                        """,
                    Inline = true
                }
            ]));
    }

    [SubSlashCommand("user", "Показать карточку участника, уровень защиты и статистику кейсов")]
    public async Task UserAsync(
        [SlashCommandParameter(Description = "Участник (по умолчанию — вы)")] User? user = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        var target = user ?? Context.User;
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<MemberHistoryReport>>(new GetMemberHistory(
            GuildId.From(Context.Guild.Id),
            MemberId.From(target.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        var p = result.Value.Profile;
        var cases = result.Value.Cases;
        var statusBadge = p switch
        {
            { IsOwner: true } => "👑 Владелец сервера (Защищён `M101`)",
            { IsBot: true } => "🤖 Системный бот (Защищён `M101`)",
            { IsStaff: true } => "🛡️ Персонал / Модератор (Защищён `M101`)",
            { OutranksBot: true } => "⭐ Роль выше бота (Защищён `M101`)",
            _ => "👤 Обычный участник (Под контролем ИИ)"
        };

        await FollowupAsync(EmbedTheme.Card(
            $"👤 Карточка участника: {target.Username}",
            $"Упоминание: <@{target.Id}> (`{target.Id}`)",
            EmbedTheme.Cyan,
            [
                new EmbedFieldProperties { Name = "🔰 Статус защиты", Value = statusBadge, Inline = false },
                new EmbedFieldProperties { Name = "📋 Всего кейсов / страйков", Value = $"`{cases.Count}`", Inline = true },
                new EmbedFieldProperties { Name = "🔨 Банов / Таймаутов", Value = $"`{cases.Count(c => c.Status is ModerationCaseStatus.Banned or ModerationCaseStatus.TimedOut)}`", Inline = true }
            ]));
    }

    [SubSlashCommand("cases", "Показать последние кейсы ИИ-модерации и действия админов на сервере")]
    public async Task CasesAsync(
        [SlashCommandParameter(Description = "Количество записей (1..20)")] int limit = 10)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<IReadOnlyList<ModerationCase>>>(
            new GetRecentGuildCases(GuildId.From(Context.Guild.Id), limit));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        if (result.Value.Count == 0)
        {
            await FollowupAsync(EmbedTheme.SuccessCard(
                "Журнал кейсов пуст",
                "На сервере ещё не зафиксировано ни одного нарушения или кейса модерации."));
            return;
        }

        var sb = new StringBuilder();
        foreach (var c in result.Value)
        {
            var icon = c.Status switch
            {
                ModerationCaseStatus.Banned => "🔨 `БАН`",
                ModerationCaseStatus.Kicked => "👢 `КИК`",
                ModerationCaseStatus.TimedOut => "🔇 `МУТ`",
                ModerationCaseStatus.Dismissed => "✅ `ИГНОР`",
                ModerationCaseStatus.Reverted => "🔓 `РАЗБАН`",
                _ => "⚠️ `ОТКРЫТ`"
            };
            sb.AppendLine($"• {icon} <@{c.MemberId.Value}> (`{c.Assessment.Confidence.Value:P0}`) — {c.Assessment.Reason.Value} _({c.OpenedAt:dd.MM HH:mm})_");
        }

        await FollowupAsync(EmbedTheme.Card(
            $"🗂️ Последние кейсы модерации ({result.Value.Count})",
            sb.ToString(),
            EmbedTheme.Primary));
    }
}
