using DiscordGeminiAdmin.Application.UseCases.Moderation.AdminActions;
using DiscordGeminiAdmin.Application.UseCases.Moderation.ChannelActions;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

/// <summary>
/// Простые верхнеуровневые команды модерации и управления чатом (без лишней вложенности).
/// </summary>
public sealed class AdminCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("clear", "Очистить последние сообщения в чате (или сообщения конкретного пользователя)")]
    public async Task ClearAsync(
        [SlashCommandParameter(Name = "count", Description = "Количество сообщений для удаления (1..100)")] int count = 20,
        [SlashCommandParameter(Name = "user", Description = "Удалять только сообщения этого участника (необязательно)")] User? user = null)
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
            user is not null ? MemberId.From(user.Id) : null,
            "Очистка чата через /clear"));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка очистки сообщений"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "🧹 Чат очищен",
            $"Удалено сообщений в <#{Context.Channel.Id}>: **`{result.Value}`**{(user is not null ? $" (от <@{user.Id}>)" : string.Empty)}."));
    }

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

    [SlashCommand("ban", "Забанить участника на сервере")]
    public async Task BanAsync(
        [SlashCommandParameter(Name = "user", Description = "Участник для бана")] User user,
        [SlashCommandParameter(Name = "reason", Description = "Причина бана")] string reason = "Нарушение правил сервера")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

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
            $"Участник <@{user.Id}> заблокирован на сервере.\n**Причина:** {reason}",
            EmbedTheme.Danger,
            ephemeral: false));
    }

    [SlashCommand("unban", "Разбанить пользователя по его Discord ID")]
    public async Task UnbanAsync(
        [SlashCommandParameter(Name = "user-id", Description = "Discord ID пользователя (число)")] string userId,
        [SlashCommandParameter(Name = "reason", Description = "Причина разбана")] string reason = "Снятие бана администратором")
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

        await RespondAsync(InteractionCallback.DeferredMessage());

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
            "🔓 Пользователь разбанен",
            $"Блокировка для <@{rawId}> (`{rawId}`) снята.",
            ephemeral: false));
    }

    [SlashCommand("kick", "Выгнать (кикнуть) участника с сервера")]
    public async Task KickAsync(
        [SlashCommandParameter(Name = "user", Description = "Участник для кика")] User user,
        [SlashCommandParameter(Name = "reason", Description = "Причина кика")] string reason = "Нарушение правил сервера")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

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
            "👢 Участник исключён",
            $"Участник <@{user.Id}> выгнан с сервера.\n**Причина:** {reason}",
            EmbedTheme.Warning,
            ephemeral: false));
    }

    [SlashCommand("mute", "Выдать мут (таймаут) участнику на указанное количество минут")]
    public async Task MuteAsync(
        [SlashCommandParameter(Name = "user", Description = "Участник")] User user,
        [SlashCommandParameter(Name = "minutes", Description = "На сколько минут (например: 10, 60, 1440)")] int minutes = 60,
        [SlashCommandParameter(Name = "reason", Description = "Причина мута")] string reason = "Нарушение правил общения")
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<Guid>>(new AdminTimeoutMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            minutes,
            reason));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось выдать мут"));
            return;
        }

        await FollowupAsync(EmbedTheme.Card(
            "🔇 Мут выдан",
            $"Участник <@{user.Id}> получил мут на **`{Math.Clamp(minutes, 1, 40320)}` мин.**\n**Причина:** {reason}",
            EmbedTheme.Warning,
            ephemeral: false));
    }

    [SlashCommand("unmute", "Снять мут (таймаут) с участника")]
    public async Task UnmuteAsync(
        [SlashCommandParameter(Name = "user", Description = "Участник")] User user)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result>(new AdminUntimeoutMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            "Снятие мута администратором"));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка снятия мута"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "🔊 Мут снят",
            $"Участник <@{user.Id}> снова может писать в чатах и говорить в голосовых комнатах.",
            ephemeral: false));
    }

    [SlashCommand("warn", "Выдать предупреждение участнику")]
    public async Task WarnAsync(
        [SlashCommandParameter(Name = "user", Description = "Участник")] User user,
        [SlashCommandParameter(Name = "reason", Description = "Причина предупреждения")] string reason)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        var result = await bus.InvokeAsync<Result<(Guid CaseId, int TotalCases)>>(new AdminWarnMember(
            GuildId.From(Context.Guild.Id),
            MemberId.From(user.Id),
            MemberId.From(Context.User.Id),
            reason,
            null));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось выдать предупреждение"));
            return;
        }

        await FollowupAsync(EmbedTheme.Card(
            "⚠️ Предупреждение",
            $"Участнику <@{user.Id}> вынесено предупреждение (всего: **`{result.Value.TotalCases}`**).\n**Причина:** {reason}",
            EmbedTheme.Warning,
            ephemeral: false));
    }

    [SlashCommand("announce", "Опубликовать красивое объявление от администрации")]
    public async Task AnnounceAsync(
        [SlashCommandParameter(Name = "title", Description = "Заголовок объявления")] string title,
        [SlashCommandParameter(Name = "message", Description = "Текст объявления")] string message,
        [SlashCommandParameter(Name = "channel", Description = "Канал для публикации (по умолчанию — текущий)")] TextGuildChannel? channel = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var targetChannelId = channel?.Id ?? Context.Channel.Id;
        var result = await bus.InvokeAsync<Result>(new PublishAnnouncement(
            GuildId.From(Context.Guild.Id),
            ChannelId.From(targetChannelId),
            MemberId.From(Context.User.Id),
            title,
            message,
            "info",
            null));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "📢 Объявление опубликовано",
            $"Анонс **«{title}»** отправлен в <#{targetChannelId}>."));
    }
}
