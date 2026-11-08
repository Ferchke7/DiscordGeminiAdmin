using System.Text;
using DiscordGeminiAdmin.Application.UseCases.Moderation.AdminActions;
using DiscordGeminiAdmin.Application.UseCases.Moderation.ChannelActions;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

[SlashCommand("admin", "Классические команды администратора и ручной модерации")]
public sealed class AdminCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("ban", "Забанить участника на сервере с записью в журнал и кнопкой разбана")]
    public async Task BanAsync(
        [SlashCommandParameter(Description = "Участник для бана")] User user,
        [SlashCommandParameter(Description = "Причина бана")] string reason = "Нарушение правил сервера")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<Guid>>(new AdminBanMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось забанить участника"));
            return;
        }

        await FollowupAsync(EmbedTheme.Card(
            "🔨 Участник забанен",
            $"Участник <@{user.Id}> (`{user.Id}`) заблокирован на сервере.",
            EmbedTheme.Danger,
            [
                new EmbedFieldProperties { Name = "🛡️ Администратор", Value = $"<@{Context.User.Id}>", Inline = true },
                new EmbedFieldProperties { Name = "🆔 ID кейса", Value = $"`{result.Value}`", Inline = true },
                new EmbedFieldProperties { Name = "📋 Причина", Value = reason, Inline = false }
            ]));
    }

    [SubSlashCommand("unban", "Разбанить пользователя по его Discord ID")]
    public async Task UnbanAsync(
        [SlashCommandParameter(Description = "Discord ID пользователя (число)")] string userId,
        [SlashCommandParameter(Description = "Причина разбана")] string reason = "Снятие блокировки администратором")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        if (!ulong.TryParse(userId.Trim(), out var rawId) || !MemberId.TryFrom(rawId, out var targetId))
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Укажите корректный числовой Discord ID пользователя.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new AdminUnbanMember(
            GuildId.From(Context.Guild.Id),
            targetId,
            MemberId.From(Context.User.Id),
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка разбана"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Пользователь разбанен",
            $"Блокировка для <@{rawId}> (`{rawId}`) успешно снята.\n**Причина:** {reason}"));
    }

    [SubSlashCommand("kick", "Выгнать (кикнуть) участника с сервера")]
    public async Task KickAsync(
        [SlashCommandParameter(Description = "Участник для кика")] User user,
        [SlashCommandParameter(Description = "Причина кика")] string reason = "Нарушение правил сообщества")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<Guid>>(new AdminKickMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось кикнуть участника"));
            return;
        }

        await FollowupAsync(EmbedTheme.Card(
            "👢 Участник исключён с сервера",
            $"Участник <@{user.Id}> (`{user.Id}`) кикнут с сервера.",
            EmbedTheme.Warning,
            [
                new EmbedFieldProperties { Name = "🛡️ Модератор", Value = $"<@{Context.User.Id}>", Inline = true },
                new EmbedFieldProperties { Name = "🆔 Кейс", Value = $"`{result.Value}`", Inline = true },
                new EmbedFieldProperties { Name = "📋 Причина", Value = reason, Inline = false }
            ]));
    }

    [SubSlashCommand("timeout", "Выдать мут (таймаут) участнику на указанное число минут")]
    public async Task TimeoutAsync(
        [SlashCommandParameter(Description = "Участник")] User user,
        [SlashCommandParameter(Description = "Длительность в минутах (например: 10, 60, 1440)")] int minutes = 60,
        [SlashCommandParameter(Description = "Причина таймаута")] string reason = "Охлаждение пыла / флуд")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<Guid>>(new AdminTimeoutMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            minutes,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось выдать таймаут"));
            return;
        }

        await FollowupAsync(EmbedTheme.Card(
            "🔇 Таймаут выдан",
            $"Участник <@{user.Id}> переведён в режим чтения на **`{Math.Clamp(minutes, 1, 40320)}` мин.**",
            EmbedTheme.Warning,
            [
                new EmbedFieldProperties { Name = "🛡️ Модератор", Value = $"<@{Context.User.Id}>", Inline = true },
                new EmbedFieldProperties { Name = "🆔 Кейс", Value = $"`{result.Value}`", Inline = true },
                new EmbedFieldProperties { Name = "📋 Причина", Value = reason, Inline = false }
            ]));
    }

    [SubSlashCommand("untimeout", "Досрочно снять мут (таймаут) с участника")]
    public async Task UntimeoutAsync(
        [SlashCommandParameter(Description = "Участник")] User user,
        [SlashCommandParameter(Description = "Причина снятия таймаута")] string reason = "Таймаут снят досрочно")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new AdminUntimeoutMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка снятия таймаута"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Таймаут снят",
            $"Участник <@{user.Id}> снова может писать в чатах и говорить в голосовых каналах."));
    }

    [SubSlashCommand("warn", "Выдать официальное предупреждение (страйк) с записью в базу")]
    public async Task WarnAsync(
        [SlashCommandParameter(Description = "Участник")] User user,
        [SlashCommandParameter(Description = "Причина предупреждения")] string reason,
        [SlashCommandParameter(Description = "Код нарушенного правила (например: R1)")] string? ruleCode = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<(Guid CaseId, int TotalCases)>>(new AdminWarnMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            reason,
            ruleCode));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось выдать предупреждение"));
            return;
        }

        var (caseId, totalCases) = result.Value;
        await FollowupAsync(EmbedTheme.Card(
            "⚠️ Предупреждение записано",
            $"Участнику <@{user.Id}> вынесено официальное предупреждение.",
            EmbedTheme.Warning,
            [
                new EmbedFieldProperties { Name = "📊 Всего кейсов/страйков", Value = $"`{totalCases}`", Inline = true },
                new EmbedFieldProperties { Name = "📜 Правило", Value = $"`{ruleCode ?? "не указано"}`", Inline = true },
                new EmbedFieldProperties { Name = "🆔 ID записи", Value = $"`{caseId}`", Inline = true },
                new EmbedFieldProperties { Name = "📋 Причина", Value = reason, Inline = false }
            ]));
    }

    [SubSlashCommand("warnings", "Показать историю предупреждений, банов и ИИ-кейсов участника")]
    public async Task WarningsAsync(
        [SlashCommandParameter(Description = "Участник")] User user)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<MemberHistoryReport>>(new GetMemberHistory(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        var cases = result.Value.Cases;
        if (cases.Count == 0)
        {
            await FollowupAsync(EmbedTheme.SuccessCard(
                "Чистая репутация",
                $"У участника <@{user.Id}> нет ни одного предупреждения или кейса модерации."));
            return;
        }

        var sb = new StringBuilder();
        foreach (var c in cases.Take(10))
        {
            var badge = c.Status switch
            {
                ModerationCaseStatus.Banned => "🔨 `БАН`",
                ModerationCaseStatus.Kicked => "👢 `КИК`",
                ModerationCaseStatus.TimedOut => "🔇 `ТАЙМАУТ`",
                ModerationCaseStatus.Dismissed => "✅ `ИГНОР`",
                ModerationCaseStatus.Reverted => "🔓 `РАЗБАН`",
                _ => "⚠️ `ОТКРЫТ / ВАРН`"
            };
            var rule = c.Assessment.Rule?.Value is { } r ? $" • `{r}`" : string.Empty;
            sb.AppendLine($"• {badge} (`{c.OpenedAt:dd.MM HH:mm}` • `{c.Assessment.Confidence.Value:P0}`{rule}) — {c.Assessment.Reason.Value}");
        }

        await FollowupAsync(EmbedTheme.Card(
            $"📋 Досье модерации: {user.Username} (всего: {cases.Count})",
            sb.ToString(),
            EmbedTheme.Primary));
    }

    [SubSlashCommand("purge", "Массово удалить последние сообщения в текущем канале (до 100 шт.)")]
    public async Task PurgeAsync(
        [SlashCommandParameter(Description = "Количество сообщений (1..100)")] int count = 10,
        [SlashCommandParameter(Description = "Удалять только сообщения этого участника (необязательно)")] User? author = null,
        [SlashCommandParameter(Description = "Причина очистки")] string reason = "Очистка чата модератором")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<int>>(new PurgeChannelMessages(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(Context.Channel.Id),
            MemberId.From(Context.User.Id),
            count,
            author is not null ? MemberId.From(author.Id) : null,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка очистки сообщений"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Чат очищен",
            $"Удалено сообщений в <#{Context.Channel.Id}>: **`{result.Value}`**{(author is not null ? $" (фильтр: <@{author.Id}>)" : string.Empty)}."));
    }

    [SubSlashCommand("slowmode", "Установить медленный режим (slowmode) в текущем канале")]
    public async Task SlowmodeAsync(
        [SlashCommandParameter(Description = "Интервал в секундах (0 = выключить, макс 21600)")] int seconds = 10,
        [SlashCommandParameter(Description = "Причина")] string reason = "Регулировка темпа чата")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new SetChannelSlowmode(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(Context.Channel.Id),
            MemberId.From(Context.User.Id),
            seconds,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            seconds <= 0 ? "Медленный режим выключен" : $"Медленный режим: {seconds} сек.",
            seconds <= 0
                ? $"В канале <#{Context.Channel.Id}> снято ограничение на частоту сообщений."
                : $"Участники в <#{Context.Channel.Id}> могут отправлять не более 1 сообщения раз в **`{seconds}` сек.**"));
    }

    [SubSlashCommand("lock", "Экстренно заблокировать текущий канал для отправки сообщений")]
    public async Task LockAsync(
        [SlashCommandParameter(Description = "Причина блокировки канала")] string reason = "Экстренная модерация / защита от рейда")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new LockUnlockChannel(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(Context.Channel.Id),
            MemberId.From(Context.User.Id),
            Locked: true,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        await FollowupAsync(EmbedTheme.Card(
            "🔒 Канал заблокирован",
            $"Отправка сообщений для `@everyone` в <#{Context.Channel.Id}> приостановлена.\nДля снятия вызовите `/admin unlock`.",
            EmbedTheme.Danger));
    }

    [SubSlashCommand("unlock", "Разблокировать текущий канал для общения")]
    public async Task UnlockAsync(
        [SlashCommandParameter(Description = "Комментарий к открытию канала")] string reason = "Канал снова открыт для общения")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new LockUnlockChannel(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(Context.Channel.Id),
            MemberId.From(Context.User.Id),
            Locked: false,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "🔓 Канал разблокирован",
            $"Участники снова могут писать в <#{Context.Channel.Id}>."));
    }

    [SubSlashCommand("announce", "Опубликовать красивое стилизованное объявление от администрации")]
    public async Task AnnounceAsync(
        [SlashCommandParameter(Description = "Канал для публикации")] TextGuildChannel channel,
        [SlashCommandParameter(Description = "Заголовок объявления")] string title,
        [SlashCommandParameter(Description = "Текст объявления")] string message,
        [SlashCommandParameter(Description = "Стиль карточки: info, warning, event, rules")] string style = "info",
        [SlashCommandParameter(Description = "Упомянуть роль (необязательно)")] Role? mentionRole = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new PublishAnnouncement(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(channel.Id),
            MemberId.From(Context.User.Id),
            title,
            message,
            style,
            mentionRole?.Id));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Объявление опубликовано",
            $"Анонс **«{title}»** успешно отправлен в канал <#{channel.Id}>."));
    }

    [SubSlashCommand("role-add", "Выдать роль участнику")]
    public async Task RoleAddAsync(
        [SlashCommandParameter(Description = "Участник")] User user,
        [SlashCommandParameter(Description = "Выдаваемая роль")] Role role,
        [SlashCommandParameter(Description = "Причина выдачи")] string reason = "Выдача роли администратором")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new ManageMemberRole(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            role.Id,
            AddRole: true,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Роль выдана",
            $"Участнику <@{user.Id}> назначена роль <@&{role.Id}>."));
    }

    [SubSlashCommand("role-remove", "Снять роль с участника")]
    public async Task RoleRemoveAsync(
        [SlashCommandParameter(Description = "Участник")] User user,
        [SlashCommandParameter(Description = "Снимаемая роль")] Role role,
        [SlashCommandParameter(Description = "Причина снятия")] string reason = "Снятие роли администратором")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new ManageMemberRole(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            role.Id,
            AddRole: false,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Роль снята",
            $"С участника <@{user.Id}> снята роль <@&{role.Id}>."));
    }
}

public sealed class LegacyCodeGuruCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("remove-messages-user", "Удалить все сообщения пользователя")]
    public async Task RemoveMessagesUserAsync(
        [SlashCommandParameter(Description = "Пользователь, чьи сообщения нужно удалить")] User user,
        [SlashCommandParameter(Description = "Максимум проверяемых сообщений (1..100)")] int count = 100)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<int>>(new PurgeChannelMessages(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(Context.Channel.Id),
            MemberId.From(Context.User.Id),
            count,
            MemberId.From(user.Id),
            $"Удаление сообщений пользователя {user.Username}"));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка удаления сообщений"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Сообщения пользователя удалены",
            $"Удалено сообщений от <@{user.Id}> в <#{Context.Channel.Id}>: **`{result.Value}`**."));
    }

    [SlashCommand("remove-messages-after", "Удалить все сообщения после указанного времени")]
    public async Task RemoveMessagesAfterAsync(
        [SlashCommandParameter(Description = "Количество последних сообщений для очистки (1..100)")] int count = 50,
        [SlashCommandParameter(Description = "Причина очистки")] string reason = "Очистка сообщений после указанного времени")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<int>>(new PurgeChannelMessages(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(Context.Channel.Id),
            MemberId.From(Context.User.Id),
            count,
             null,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка удаления сообщений"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Сообщения удалены",
            $"Удалено последних сообщений в <#{Context.Channel.Id}>: **`{result.Value}`**."));
    }
}
