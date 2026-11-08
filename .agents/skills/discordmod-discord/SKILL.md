---
name: discordmod-discord
description: >-
  Use when writing, changing or reviewing Discord Presentation or Infrastructure code in DiscordGeminiAdmin: NetCord Gateway intents, ApplicationCommandModule (slash commands), ComponentInteractionModule (buttons/select menus), gateway event handlers, ephemeral responses, deferral (>3s), Discord API limits, role hierarchy checks, and IGuildGateway adapters. Do NOT use for Domain rules (discordmod-domain), Gemini prompts (discordmod-ai), or pgvector RAG (discordmod-rag).
---

# discordmod-discord

Стек Discord: **NetCord** (`NetCord`, `NetCord.Hosting`, `NetCord.Hosting.Services`, `1.0.0-beta.30`).

## Non-negotiables

1. **Никаких побочных эффектов напрямую из Presentation.** Slash-команды, кнопки и gateway-события в `Presentation/Discord/` только парсят ввод и вызывают `IMessageBus.InvokeAsync<Result>(command)`. Все вызовы `RestClient` (`BanGuildUserAsync`, `UnbanGuildUserAsync`, создание каналов/ролей, отправка дайджестов) живут в `Infrastructure/Discord/` за портами `IGuildGateway` / `IDiscordNotifier`. *Prevents:* обход `ModerationPolicy` и нетестируемые команды.
2. **Проверка иерархии ролей и прав перед модерацией.** В `NetCordGuildGateway.GetMemberProfileAsync` всегда проверяй:
   - `guild.OwnerId == memberId.Value` (`IsOwner`);
   - `member.IsBot` (`IsBot`);
   - права ролей (`Administrator | ManageGuild | BanUsers | KickUsers | ModerateUsers | ManageMessages` → `IsStaff`);
   - `memberTop.CompareTo(botTop) >= 0` (`OutranksBot` — в `NetCord` `Role.Position` имеет тип `RolePosition`, сравнивай через `CompareTo`, равная позиция тоже считается защищённой).
   *Prevents:* попытки забанить владельца/модератора или падение с `403 Missing Permissions`.
3. **Укладывайся в 3 секунды или делай Defer.** Любая команда/кнопка, которая вызывает Gemini, RAG или несколько REST-запросов, обязана сначала отправить deferral (`RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral))`), а результат прислать через `FollowupAsync` / `ModifyResponseAsync`. *Prevents:* `Unknown interaction (10062)` при долгих ответах LLM.
4. **Привилегированные интенты включаются явно.** Для чтения сообщений и ролей в `AddDiscordGateway` нужны `GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.GuildUsers | GatewayIntents.GuildModeration`. Напомни включить `Message Content Intent` и `Server Members Intent` в Discord Developer Portal.
5. **Соблюдай лимиты Discord API:**
   - Сообщение ≤ 2000 символов; описание embed ≤ 4096; сумма по всем embed в сообщении ≤ 6000; полей в embed ≤ 25.
   - Кнопок в `ActionRow` ≤ 5; рядов `ActionRow` ≤ 5; `custom_id` ≤ 100 символов.
   - В часовом дайджесте выводи не более 5–10 кейсов на одно сообщение (разбивай на страницы или треды при превышении).
   *Prevents:* `400 Bad Request` от Discord REST при отправке отчёта.
6. **Формат `custom_id` кнопок:** `"<action>:<guid>"` (например, `"ban:0195..."`, `"unban:0195..."`, `"dismiss:0195..."`, `"proposal-approve:0195..."`). В `ComponentInteractionModule<ButtonInteractionContext>` используй атрибут `[ComponentInteraction("ban")]` и принимай `Guid caseId` параметром метода — NetCord сам распарсит сегмент после `:`.
7. **Причина в Audit Log.** В `BanGuildUserAsync` / `UnbanGuildUserAsync` / изменениях каналов всегда передавай `new RestRequestProperties { AuditLogReason = reason }`.

## Шаблон: регистрация в `Presentation/Discord/DependencyInjection.cs`

```csharp
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDiscord()
        {
            services.AddDiscordGateway(options =>
            {
                options.Intents = GatewayIntents.Guilds
                                  | GatewayIntents.GuildMessages
                                  | GatewayIntents.MessageContent
                                  | GatewayIntents.GuildUsers
                                  | GatewayIntents.GuildModeration;
            });
            services.AddApplicationCommands();
            services.AddComponentInteractions<NetCord.ButtonInteraction, ButtonInteractionContext>();
            services.AddGatewayHandlers(typeof(DependencyInjection).Assembly);
            return services;
        }
    }

    extension(IHost host)
    {
        public IHost UseDiscord()
        {
            host.AddModules(typeof(DependencyInjection).Assembly);
            return host;
        }
    }
}
```

> Внимание: в `NetCord.Hosting` `1.0.0-beta.30` метод `AddGatewayHandlers(Assembly)` вызывается на `IServiceCollection`, а `AddModules(Assembly)` — на `IHost`. Не вызывай несуществующий `host.UseGatewayHandlers()`.

## Шаблон: Slash-команда и кнопка

```csharp
using DiscordGeminiAdmin.Application.UseCases.Moderation.Ban;
using DiscordGeminiAdmin.Domain.Moderation;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Components;

public sealed class DigestButtons(IMessageBus bus) : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("ban")]
    public async Task<InteractionMessageProperties> BanAsync(Guid caseId)
    {
        var result = await bus.InvokeAsync<Result>(new BanMember(caseId, BanSource.Moderator));
        return new InteractionMessageProperties
        {
            Content = result.IsSuccess
                ? "Участник забанен."
                : string.Join("; ", result.Errors.Select(e => e.Message)),
            Flags = MessageFlags.Ephemeral
        };
    }
}
```

## Шаблон: адаптер `NetCordGuildGateway` (`Infrastructure/Discord/`)

См. полный проверенный код в `discordmod-feature/resources/reference-slice.md` (`BanGuildUserAsync`, `UnbanGuildUserAsync`, проверка `RolePosition.CompareTo`).
