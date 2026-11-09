using System.Text;
using DiscordGeminiAdmin.Application.UseCases.Moderation.AdminActions;
using DiscordGeminiAdmin.Application.UseCases.Moderation.ChannelActions;
using DiscordGeminiAdmin.Application.UseCases.Structure.Analyze;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

/// <summary>
/// Простые верхнеуровневые команды для быстрого подъёма сервера с нуля,
/// организации голосовых комнат, групп (категорий), каналов и ролей.
/// </summary>
public sealed class OrganizeCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
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

    [SlashCommand("setup", "Поднять сервер под тематику: ИИ сразу создаст группы, голосовые, текстовые каналы с описаниями и роли")]
    public async Task SetupAsync(
        [SlashCommandParameter(Name = "theme", Description = "Тематика и пожелания (например: Айти сообщество, созвоны, бэкенд и фронтенд)")] string theme,
        [SlashCommandParameter(Name = "reset", Description = "Удалить старые каналы, группы и роли перед созданием новых")] bool reset = false)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var goal = $"Подними и организуй сервер под тематику «{theme}»: создай удобные группы (категории), текстовые каналы с подробными описаниями и правилами внутри каждого канала, голосовые комнаты для общения/созвонов и базовые роли.";
        var result = await bus.InvokeAsync<Result<StructureProposal>>(new AnalyzeGuildStructure(
            GuildId.From(Context.Guild!.Id),
            Goal: goal,
            FallbackChannelId: ChannelId.From(Context.Channel.Id),
            ApplyImmediately: true,
            NewTheme: theme,
            ResetExisting: reset));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось автоматически поднять структуру сервера"));
            return;
        }

        await FollowupAsync(BuildAppliedStructureCard(
            reset ? $"🔄 Сервер полностью пересобран: {theme}" : $"🚀 Сервер настроен под тематику: {theme}",
            result.Value));
    }

    [SlashCommand("reset-setup", "Удалить все старые каналы, группы и роли и заново пересобрать сервер с полными описаниями")]
    public async Task ResetSetupAsync(
        [SlashCommandParameter(Name = "theme", Description = "Новая тематика и пожелания (оставьте пустым, чтобы пересобрать по текущей тематике)")] string? theme = null)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var effectiveTheme = string.IsNullOrWhiteSpace(theme) ? null : theme.Trim();
        var goal = effectiveTheme is not null
            ? $"Полностью пересобери сервер с нуля под тематику «{effectiveTheme}»: создай категории, текстовые каналы (обязательно с подробными правилами и описаниями для каждого канала), голосовые комнаты и роли."
            : "Полностью пересобери сервер с нуля под его тематику: создай категории, текстовые каналы (обязательно с подробными правилами и описаниями для каждого канала), голосовые комнаты и роли.";

        var result = await bus.InvokeAsync<Result<StructureProposal>>(new AnalyzeGuildStructure(
            GuildId.From(Context.Guild!.Id),
            Goal: goal,
            FallbackChannelId: ChannelId.From(Context.Channel.Id),
            ApplyImmediately: true,
            NewTheme: effectiveTheme,
            ResetExisting: true));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось выполнить сброс и пересборку сервера"));
            return;
        }

        await FollowupAsync(BuildAppliedStructureCard(
            $"🔄 Сервер полностью очищен и пересобран{(effectiveTheme is not null ? $": {effectiveTheme}" : string.Empty)}",
            result.Value));
    }

    [SlashCommand("ai", "Универсальный ИИ: напишет код, ответит на вопрос, найдёт в истории чата или организует сервер")]
    public async Task AiOrganizeAsync(
        [SlashCommandParameter(Name = "prompt", Description = "Ваш запрос: код, вопрос, поиск по истории или организация голосовых/групп")] string prompt)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<StructureProposal>>(new AnalyzeGuildStructure(
            GuildId.From(Context.Guild!.Id),
            Goal: prompt,
            FallbackChannelId: ChannelId.From(Context.Channel.Id),
            ApplyImmediately: true));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка выполнения запроса ИИ"));
            return;
        }

        await FollowupAsync(BuildAppliedStructureCard(
            result.Value.Changes.Count > 0 ? "🤖 ИИ выполнил организацию сервера" : "🤖 Ответ Code-Guru AI",
            result.Value));
    }

    [SlashCommand("code", "Попросить ИИ написать код, решить задачу или исправить ошибку")]
    public async Task CodeAsync(
        [SlashCommandParameter(Name = "task", Description = "Что нужно написать или исправить (например: REST контроллер на Spring Boot)")] string task,
        [SlashCommandParameter(Name = "language", Description = "Язык программирования (например: C#, Java, Python, TypeScript, SQL)")] string? language = null)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var langPart = string.IsNullOrWhiteSpace(language) ? string.Empty : $" (язык: {language})";
        var prompt = $"Напиши чистый, рабочий код с кратким объяснением{langPart}: {task}. Не меняй каналы сервера (changes = []).";

        var result = await bus.InvokeAsync<Result<StructureProposal>>(new AnalyzeGuildStructure(
            GuildId.From(Context.Guild!.Id),
            Goal: prompt,
            FallbackChannelId: ChannelId.From(Context.Channel.Id),
            ApplyImmediately: true));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка генерации кода"));
            return;
        }

        await FollowupAsync(BuildAppliedStructureCard(
            $"💻 Код и решение{(string.IsNullOrWhiteSpace(language) ? string.Empty : $" ({language})")}",
            result.Value));
    }

    [SlashCommand("ask", "Задать любой вопрос ИИ-ассистенту")]
    public async Task AskAsync(
        [SlashCommandParameter(Name = "question", Description = "Ваш вопрос по программированию, серверу или любой теме")] string question)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<StructureProposal>>(new AnalyzeGuildStructure(
            GuildId.From(Context.Guild!.Id),
            Goal: $"Ответь подробно на вопрос пользователя (не меняя каналы сервера, changes = []): {question}",
            FallbackChannelId: ChannelId.From(Context.Channel.Id),
            ApplyImmediately: true));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка ответа ИИ"));
            return;
        }

        await FollowupAsync(BuildAppliedStructureCard(
            $"🧠 Ответ ИИ: {(question.Length > 80 ? question[..80] + "..." : question)}",
            result.Value));
    }

    [SlashCommand("history", "Найти сообщение, тему или код в истории этого чата или сделать сводку переписки")]
    public async Task HistoryAsync(
        [SlashCommandParameter(Name = "query", Description = "Что найти в истории чата (оставьте пустым для общей выжимки переписки)")] string? query = null)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var goal = string.IsNullOrWhiteSpace(query)
            ? "Проанализируй историю сообщений этого канала (<channel_history>) и сделай краткую, понятную сводку: о чём общались участники, какие вопросы и выводы были."
            : $"Найди в истории сообщений этого канала (<channel_history>) информацию по запросу: «{query}». Укажи, кто, когда и что именно писал.";

        var result = await bus.InvokeAsync<Result<StructureProposal>>(new AnalyzeGuildStructure(
            GuildId.From(Context.Guild!.Id),
            Goal: goal,
            FallbackChannelId: ChannelId.From(Context.Channel.Id),
            ApplyImmediately: true,
            IncludeChannelHistory: true));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка поиска по истории чата"));
            return;
        }

        await FollowupAsync(BuildAppliedStructureCard(
            string.IsNullOrWhiteSpace(query) ? "📜 Сводка истории чата" : $"🔍 Поиск по истории чата: {query}",
            result.Value));
    }

    [SlashCommand("voice", "Создать голосовой канал (комнату) в нужной группе")]
    public async Task CreateVoiceAsync(
        [SlashCommandParameter(Name = "name", Description = "Название голосового канала (например: 🎙️ Общий созвон)")] string name,
        [SlashCommandParameter(Name = "group", Description = "Название группы/категории (если её нет — создастся автоматически)")] string? group = "🔊 Голосовые каналы")
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<ChannelId>>(new CreateServerChannel(
            GuildId.From(Context.Guild!.Id),
            MemberId.From(Context.User.Id),
            name,
            group,
            Topic: null,
            IsVoice: true));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка создания голосового канала"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "🔊 Голосовой канал создан",
            $"Создан голосовой канал <#{result.Value.Value}>{(string.IsNullOrWhiteSpace(group) ? string.Empty : $" в группе **`{group}`**")}.",
            ephemeral: false));
    }

    [SlashCommand("group", "Создать группу (категорию) и сразу добавить в неё текстовые и голосовые каналы")]
    public async Task CreateGroupAsync(
        [SlashCommandParameter(Name = "name", Description = "Название новой группы (например: 💻 Разработка)")] string name,
        [SlashCommandParameter(Name = "text-channels", Description = "Текстовые каналы через запятую (например: общий, вопросы, код-ревью)")] string? textChannels = null,
        [SlashCommandParameter(Name = "voice-channels", Description = "Голосовые каналы через запятую (например: Дейлик, Парный кодинг)")] string? voiceChannels = null)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<int>>(new CreateServerGroup(
            GuildId.From(Context.Guild!.Id),
            name,
            textChannels,
            voiceChannels));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка создания группы каналов"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            $"📁 Группа «{name}» создана",
            $"Успешно создана группа **`{name}`**"
            + (string.IsNullOrWhiteSpace(textChannels) ? string.Empty : $"\n💬 **Текстовые:** `{textChannels}`")
            + (string.IsNullOrWhiteSpace(voiceChannels) ? string.Empty : $"\n🔊 **Голосовые:** `{voiceChannels}`"),
            ephemeral: false));
    }

    [SlashCommand("channel", "Создать текстовый канал в нужной группе")]
    public async Task CreateTextChannelAsync(
        [SlashCommandParameter(Name = "name", Description = "Название текстового канала (например: новости)")] string name,
        [SlashCommandParameter(Name = "group", Description = "Название группы/категории (если нет — создастся автоматически)")] string? group = null,
        [SlashCommandParameter(Name = "topic", Description = "Описание канала")] string? topic = null)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<ChannelId>>(new CreateServerChannel(
            GuildId.From(Context.Guild!.Id),
            MemberId.From(Context.User.Id),
            name,
            group,
            topic,
            IsVoice: false));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка создания текстового канала"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "💬 Текстовый канал создан",
            $"Создан канал <#{result.Value.Value}>{(string.IsNullOrWhiteSpace(group) ? string.Empty : $" в группе **`{group}`**")}.",
            ephemeral: false));
    }

    [SlashCommand("role", "Создать новую роль на сервере (и по желанию сразу выдать участнику)")]
    public async Task CreateOrAssignRoleAsync(
        [SlashCommandParameter(Name = "name", Description = "Название роли (например: Разработчик)")] string name,
        [SlashCommandParameter(Name = "user", Description = "Участник, которому сразу выдать эту роль (необязательно)")] User? user = null)
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<ulong>>(new CreateOrAssignServerRole(
            GuildId.From(Context.Guild!.Id),
            name,
            user is not null ? MemberId.From(user.Id) : null));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка создания или выдачи роли"));
            return;
        }

        var desc = user is not null
            ? $"Роль <@&{result.Value}> готова и выдана участнику <@{user.Id}>."
            : $"Роль <@&{result.Value}> успешно создана на сервере.";

        await FollowupAsync(EmbedTheme.SuccessCard("🎭 Роль готова", desc, ephemeral: false));
    }

    [SlashCommand("server", "Показать текущую структуру сервера: группы, голосовые, текстовые каналы и роли")]
    public async Task ServerOverviewAsync()
    {
        if (!await EnsureModeratorAsync())
            return;

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<GuildDashboardReport>>(
            new GetGuildDashboard(GuildId.From(Context.Guild!.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        var d = result.Value;
        var textChannels = d.Structure?.Channels.Where(c => !c.IsVoice).Select(c => $"#{c.Name}").ToList() ?? [];
        var voiceChannels = d.Structure?.Channels.Where(c => c.IsVoice).Select(c => $"🔊 {c.Name}").ToList() ?? [];
        var categories = d.Structure?.Categories ?? [];
        var roles = d.Structure?.Roles ?? [];

        await FollowupAsync(EmbedTheme.Card(
            $"📊 Сервер: {d.Structure?.GuildName ?? Context.Guild.Name}",
            $"**Тематика:** `{d.Profile?.Theme.Value ?? "Не задана (вызовите /setup)"}`",
            EmbedTheme.Primary,
            [
                new EmbedFieldProperties
                {
                    Name = $"📁 Группы ({categories.Count})",
                    Value = categories.Count > 0 ? string.Join(", ", categories.Take(15).Select(c => $"`{c}`")) : "`нет групп`",
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = $"🔊 Голосовые каналы ({voiceChannels.Count})",
                    Value = voiceChannels.Count > 0 ? string.Join(", ", voiceChannels.Take(15).Select(c => $"`{c}`")) : "`нет голосовых`",
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = $"💬 Текстовые каналы ({textChannels.Count})",
                    Value = textChannels.Count > 0 ? string.Join(", ", textChannels.Take(20).Select(c => $"`{c}`")) : "`нет каналов`",
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = $"🎭 Роли ({roles.Count})",
                    Value = roles.Count > 0 ? string.Join(", ", roles.Take(15).Select(r => $"`{r}`")) : "`нет ролей`",
                    Inline = false
                }
            ],
            ephemeral: false));
    }

    internal static InteractionMessageProperties BuildAppliedStructureCard(string title, StructureProposal proposal)
    {
        if (proposal.Changes.Count == 0)
        {
            var answerText = proposal.Summary.Length > 3950
                ? proposal.Summary[..3950] + "\n..."
                : proposal.Summary;

            return EmbedTheme.Card(
                title,
                answerText,
                EmbedTheme.Purple,
                ephemeral: false);
        }

        var sb = new StringBuilder();
        sb.AppendLine(proposal.Summary);
        sb.AppendLine();
        sb.AppendLine($"**Выполнено действий на сервере (`{proposal.Changes.Count}`):**");

        foreach (var change in proposal.Changes.Take(20))
        {
            var icon = change.Kind switch
            {
                ProposedChangeKind.CreateCategory => "📁 Группа:",
                ProposedChangeKind.CreateVoiceChannel => "🔊 Голосовой:",
                ProposedChangeKind.CreateTextChannel => "💬 Канал:",
                ProposedChangeKind.CreateRole => "🎭 Роль:",
                ProposedChangeKind.RenameChannel => "✏️ Переименован:",
                ProposedChangeKind.UpdateChannelTopic => "📝 Описание:",
                _ => "•"
            };
            var groupInfo = !string.IsNullOrWhiteSpace(change.ParentCategory)
                ? $" _(в группе `{change.ParentCategory}`)_"
                : string.Empty;
            sb.AppendLine($"{icon} **`{change.TargetName}`**{groupInfo}");
        }

        return EmbedTheme.Card(
            title,
            sb.ToString(),
            EmbedTheme.Success,
            ephemeral: false);
    }
}
