using DiscordGeminiAdmin.Presentation.Discord.Components;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

public sealed class HelpCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("help", "Список всех команд бота для настройки сервера, голосовых комнат и модерации")]
    public async Task<InteractionMessageProperties> HelpAsync()
    {
        if (Context.Guild is null)
            return EmbedTheme.ErrorCard("Команда доступна только на сервере.");

        if (!await ModeratorGuard.IsModeratorAsync(Context.User, Context.Guild, Context.Guild.Id, Context.Client.Rest))
            return ModeratorGuard.AccessDeniedCard();

        return HelpSectionBuilder.BuildMessage("overview", ephemeral: true);
    }
}

internal static class HelpSectionBuilder
{
    public static InteractionMessageProperties BuildMessage(string section, bool ephemeral)
    {
        _ = section;
        return EmbedTheme.Card(
            "🤖 Команды бота Code-Guru",
            "Все команды доступны прямо через **`/`** (без лишних подменю) или просто напишите в чат **`@Code-Guru <что сделать>`**:",
            EmbedTheme.Primary,
            [
                new EmbedFieldProperties
                {
                    Name = "🧠 ИИ-Помощник: Код, Вопросы и История чата",
                    Value = """
                        • **`/ai`** `prompt` *(или `@Code-Guru ...` в чате)* — универсальный ИИ: напишет код, ответит на вопрос, найдёт в истории или организует сервер
                        • **`/code`** `task` `[language]` — написать код, функцию или исправить ошибку (C#, Java, Python, JS, SQL)
                        • **`/ask`** `question` — задать любой вопрос ИИ
                        • **`/history`** `[query]` — найти сообщение/тему в истории переписки канала или сделать сводку чата
                        """,
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = "🚀 Поднять и организовать сервер (ИИ + вручную)",
                    Value = """
                        • **`/setup`** `theme` `[reset]` — поднять сервер с нуля под тематику (с подробными описаниями внутри каждого канала)
                        • **`/reset-setup`** `[theme]` — полностью удалить старые каналы/группы/роли и заново пересобрать сервер с описаниями
                        • **`/voice`** `name` `[group]` — быстро создать голосовой канал в нужной группе
                        • **`/group`** `name` `[text-channels]` `[voice-channels]` — создать группу (категорию) и сразу добавить в неё каналы
                        • **`/channel`** `name` `[group]` `[topic]` — создать текстовый канал в группе
                        • **`/role`** `name` `[user]` — создать новую роль (и сразу выдать участнику)
                        • **`/server`** — посмотреть текущие группы, голосовые, текстовые каналы и роли сервера
                        """,
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = "🛡️ Модерация и очистка чата",
                    Value = """
                        • **`/clear`** `[count]` `[user]` — очистить последние сообщения в чате
                        • **`/remove-messages-user`** `user` — удалить сообщения конкретного пользователя
                        • **`/remove-messages-after`** `count` — удалить последние сообщения
                        • **`/ban`** `user` `[reason]` / **`/unban`** `user-id` — бан и разбан
                        • **`/kick`** `user` `[reason]` — выгнать участника с сервера
                        • **`/mute`** `user` `[minutes]` / **`/unmute`** `user` — мут и снятие мута
                        • **`/warn`** `user` `reason` — выдать предупреждение
                        • **`/announce`** `title` `message` — опубликовать объявление
                        """,
                    Inline = false
                }
            ],
            ephemeral: ephemeral);
    }
}
