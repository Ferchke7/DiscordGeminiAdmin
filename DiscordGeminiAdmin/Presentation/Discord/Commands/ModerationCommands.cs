using DiscordGeminiAdmin.Application.UseCases.Guilds.Setup;
using DiscordGeminiAdmin.Application.UseCases.Guilds.UpdateSettings;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.IndexDocument;
using DiscordGeminiAdmin.Application.UseCases.Moderation.ChannelActions;
using DiscordGeminiAdmin.Application.UseCases.Moderation.RunHourly;
using DiscordGeminiAdmin.Application.UseCases.Moderation.ScanMember;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.Presentation.Discord.Mapper;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

[SlashCommand("mod", "Управление ИИ-модератором сервера и анализом чата")]
public sealed class ModerationCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("setup", "Первичная настройка тематики сервера и канала отчётов")]
    public async Task<InteractionMessageProperties> SetupAsync(
        [SlashCommandParameter(Description = "Тематика сервера (например: C# и .NET разработка)")] string theme,
        [SlashCommandParameter(Description = "Канал для отчётов и дайджестов модерации")] TextGuildChannel modChannel,
        [SlashCommandParameter(Description = "Dry-run режим (true = не банить реально, только писать в отчёт)")] bool dryRun = true)
    {
        if (Context.Guild is null)
            return EmbedTheme.ErrorCard("Команда доступна только на сервере.");

        var result = await bus.InvokeAsync<Result>(new SetupGuild(
            GuildId.From(Context.Guild.Id),
            theme,
            ChannelId.From(modChannel.Id),
            dryRun));

        if (result.IsFailed)
            return EmbedTheme.ErrorCard(result, "Ошибка настройки сервера");

        return EmbedTheme.SuccessCard(
            "ИИ-Модератор настроен для сервера",
            "Конфигурация сохранена в базе данных. Почасовой мониторинг активен.",
            [
                new EmbedFieldProperties { Name = "🎯 Тематика", Value = theme, Inline = false },
                new EmbedFieldProperties { Name = "📢 Канал модераторов", Value = $"<#{modChannel.Id}>", Inline = true },
                new EmbedFieldProperties { Name = "🧪 Режим Dry-Run", Value = dryRun ? "`ВКЛ (безопасный тест)`" : "`ВЫКЛ (боевой авто-бан)`", Inline = true }
            ]);
    }

    [SubSlashCommand("settings", "Изменить пороги автоматического бана")]
    public async Task<InteractionMessageProperties> UpdateSettingsAsync(
        [SlashCommandParameter(Description = "Мин. уверенность ИИ для авто-бана (0.5..1.0)")] double? minConfidence = null,
        [SlashCommandParameter(Description = "Мин. сообщений-доказательств")] int? minEvidence = null,
        [SlashCommandParameter(Description = "Макс. авто-банов в час")] int? maxPerHour = null,
        [SlashCommandParameter(Description = "Режим DryRun (true/false)")] bool? dryRun = null)
    {
        if (Context.Guild is null)
            return EmbedTheme.ErrorCard("Команда доступна только на сервере.");

        var result = await bus.InvokeAsync<Result<ModerationPolicy>>(new UpdateGuildSettings(
            GuildId.From(Context.Guild.Id),
            minConfidence,
            minEvidence,
            maxPerHour,
            dryRun));

        if (result.IsFailed)
            return EmbedTheme.ErrorCard(result, "Ошибка обновления политики");

        var p = result.Value;
        return EmbedTheme.SuccessCard(
            "Политика авто-бана обновлена",
            "Новые пороги вступили в силу для всех следующих проверок ИИ.",
            [
                new EmbedFieldProperties { Name = "🎯 Мин. уверенность", Value = $"`{p.MinConfidence:P0}`", Inline = true },
                new EmbedFieldProperties { Name = "📨 Мин. доказательств", Value = $"`{p.MinEvidenceMessages}` сообщ.", Inline = true },
                new EmbedFieldProperties { Name = "⏱️ Лимит банов/час", Value = $"`{p.MaxAutoBansPerHour}`", Inline = true },
                new EmbedFieldProperties { Name = "🧪 Режим Dry-Run", Value = p.DryRun ? "`ВКЛ (только отчёт)`" : "`ВЫКЛ (реальный бан)`", Inline = false }
            ]);
    }

    [SubSlashCommand("scan", "Внеочередная ИИ-проверка недавних сообщений участника с кнопками решений")]
    public async Task ScanAsync(
        [SlashCommandParameter(Description = "Проверяемый участник")] User user)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<ModerationCase?>>(new ScanMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка сканирования участника"));
            return;
        }

        if (result.Value is null)
        {
            await FollowupAsync(EmbedTheme.SuccessCard(
                "Нарушений не обнаружено",
                $"За последние 24 часа у <@{user.Id}> не найдено подозрительных сообщений или нарушений правил сервера."));
            return;
        }

        var dto = ModerationCaseOverviewMapper.ToDto(result.Value);
        var actionRow = new ActionRowProperties([
            new ButtonProperties($"ban:{dto.Id}", "🔨 Бан", ButtonStyle.Danger),
            new ButtonProperties($"kick:{dto.Id}", "👢 Кик", ButtonStyle.Primary),
            new ButtonProperties($"timeout:{dto.Id}", "🔇 Таймаут 1ч", ButtonStyle.Secondary),
            new ButtonProperties($"dismiss:{dto.Id}", "✅ Игнор", ButtonStyle.Success)
        ]);

        await FollowupAsync(EmbedTheme.Card(
            $"🔍 Результат ИИ-аудита: {user.Username}",
            $"Участник: <@{dto.MemberId.Value}>\n**Вердикт Gemini:** {dto.Assessment.Reason.Value}",
            EmbedTheme.Warning,
            [
                new EmbedFieldProperties { Name = "🎯 Уверенность", Value = $"`{dto.Assessment.Confidence.Value:P0}`", Inline = true },
                new EmbedFieldProperties { Name = "📜 Правило (RAG)", Value = $"`{dto.Assessment.Rule?.Value ?? "не указано"}`", Inline = true },
                new EmbedFieldProperties { Name = "🆔 ID Кейса", Value = $"`{dto.Id}`", Inline = true }
            ],
            [actionRow]));
    }

    [SubSlashCommand("digest-now", "Запустить часовой прогон ИИ-модерации прямо сейчас")]
    public async Task DigestNowAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<RunHourlyModerationSummary>>(
            new RunHourlyModeration(GuildId.From(Context.Guild.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка часового прогона"));
            return;
        }

        var s = result.Value;
        await FollowupAsync(EmbedTheme.SuccessCard(
            "Часовой ИИ-прогон завершён",
            "Все накопленные сообщения проанализированы через Gemini и сопоставлены с RAG-базой.",
            [
                new EmbedFieldProperties { Name = "📂 Открыто кейсов", Value = $"`{s.CasesOpened}`", Inline = true },
                new EmbedFieldProperties { Name = "🔨 Авто-банов", Value = $"`{s.AutoBannedCount}`", Inline = true },
                new EmbedFieldProperties { Name = "📊 Дайджест в мод-канале", Value = $"`{s.DigestId?.ToString() ?? "не требовался (нарушений нет)"}`", Inline = false }
            ]));
    }

    [SubSlashCommand("summarize", "Сделать ИИ-выжимку (саммари) обсуждений в текущем канале")]
    public async Task SummarizeAsync(
        [SlashCommandParameter(Description = "За сколько последних часов проанализировать переписку (1..48)")] int hours = 12)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var channelName = (Context.Channel as IGuildChannel)?.Name ?? Context.Channel.Id.ToString();
        var result = await bus.InvokeAsync<Result<string>>(new SummarizeChannelMessages(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(Context.Channel.Id),
            channelName,
            hours));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка генерации саммари"));
            return;
        }

        await FollowupAsync(EmbedTheme.Card(
            $"📝 ИИ-выжимка канала #{channelName} (за {Math.Clamp(hours, 1, 48)} ч.)",
            result.Value,
            EmbedTheme.Purple));
    }
}

[SlashCommand("mod-config", "Быстрая настройка тематики и каналов ИИ-модератора")]
public sealed class ModConfigCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("set-theme", "Задать тематику и подробное описание сообщества для ИИ и организатора")]
    public async Task SetThemeAsync(
        [SlashCommandParameter(Description = "Тематика сервера (например: IT сообщество)")] string theme,
        [SlashCommandParameter(Description = "Подробное описание сервера и пожелания к каналам")] string? description = null,
        [SlashCommandParameter(Description = "Канал отчётов модерации (по умолчанию — текущий)")] TextGuildChannel? modChannel = null,
        [SlashCommandParameter(Description = "Режим Dry-Run (true/false)")] bool? dryRun = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var guildId = GuildId.From(Context.Guild.Id);
        var fallbackChannel = ChannelId.From(modChannel?.Id ?? Context.Channel.Id);
        var fullTheme = string.IsNullOrWhiteSpace(description) ? theme : $"{theme} — {description}";

        var result = await bus.InvokeAsync<Result<ChannelId>>(new SetGuildTheme(
            guildId,
            fullTheme,
            fallbackChannel,
            dryRun));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка сохранения тематики"));
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

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Тематика и концепция сервера сохранены",
            "Конфигурация обновлена и проиндексирована в RAG. Для построения структуры каналов под эту тематику вызовите `/organize analyze`.",
            [
                new EmbedFieldProperties { Name = "🎯 Тематика", Value = theme, Inline = true },
                new EmbedFieldProperties { Name = "📢 Канал модерации", Value = $"<#{result.Value.Value}>", Inline = true },
                new EmbedFieldProperties { Name = "📝 Описание в RAG", Value = string.IsNullOrWhiteSpace(description) ? "`не указано`" : description, Inline = false }
            ],
            ephemeral: false));
    }

    [SubSlashCommand("set-channels", "Назначить канал модераторов и канал логов")]
    public async Task<InteractionMessageProperties> SetChannelsAsync(
        [SlashCommandParameter(Name = "mod-channel", Description = "Канал для отчётов и дайджестов модерации")] TextGuildChannel modChannel,
        [SlashCommandParameter(Name = "log-channel", Description = "Дополнительный канал логов (необязательно)")] TextGuildChannel? logChannel = null)
    {
        if (Context.Guild is null)
            return EmbedTheme.ErrorCard("Команда доступна только на сервере.");

        var result = await bus.InvokeAsync<Result<string>>(new SetGuildChannels(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(modChannel.Id)));

        if (result.IsFailed)
            return EmbedTheme.ErrorCard(result, "Ошибка настройки каналов");

        return EmbedTheme.SuccessCard(
            "Каналы модерации назначены",
            "Отчёты ИИ-модератора, карточки авто-банов и планы структуры будут публиковаться в выбранный канал.",
            [
                new EmbedFieldProperties { Name = "🛡️ Мод-канал", Value = $"<#{modChannel.Id}>", Inline = true },
                new EmbedFieldProperties { Name = "📋 Лог-канал", Value = logChannel is not null ? $"<#{logChannel.Id}>" : $"<#{modChannel.Id}>", Inline = true },
                new EmbedFieldProperties { Name = "🎯 Текущая тематика", Value = result.Value, Inline = false }
            ],
            ephemeral: false);
    }
}
