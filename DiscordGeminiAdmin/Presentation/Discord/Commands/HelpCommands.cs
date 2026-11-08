using DiscordGeminiAdmin.Presentation.Discord.Components;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

public sealed class HelpCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("help", "Список всех команд бота для настройки сервера, голосовых комнат и модерации")]
    public InteractionMessageProperties Help() => HelpSectionBuilder.BuildMessage("overview", ephemeral: false);
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
                    Name = "🚀 Поднять и организовать сервер (ИИ + вручную)",
                    Value = """
                        • **`/setup`** `theme` — поднять сервер с нуля под тематику (ИИ сам создаст группы, голосовые, текстовые каналы и роли)
                        • **`/ai`** `prompt` — попросить ИИ сделать что угодно: организовать голосовые комнаты, добавить группы, каналы или роли
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
