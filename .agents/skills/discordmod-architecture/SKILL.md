---
name: discordmod-architecture
description: >-
  Use when writing, changing or reviewing code that touches the architecture contract of DiscordGeminiAdmin: layers (Domain, Application, Infrastructure, Presentation), allowed dependencies, folder and namespace layout, naming, DI extension blocks, Result and error codes, Wolverine handler discovery, the DGA0001 analyzer. Do NOT use for aggregate design (discordmod-domain), a full feature slice (discordmod-feature), Discord commands (discordmod-discord), Gemini prompts (discordmod-ai) or vector search (discordmod-rag).
---

# discordmod-architecture

Эталон в `C:\care-call1\services\appointment\src\CC.AppointmentService` (ветка `develop`). Если сомневаешься в форме - открой аналог там.

## Слои и зависимости

```text
Presentation ──(IMessageBus)──► Application ──► Domain ◄── SharedKernel
                                     ▲
Infrastructure ──(реализует порты)───┘
```

| Слой | Папка | Что внутри | Не может ссылаться на |
|---|---|---|---|
| SharedKernel | `SharedKernel/` | `Entity`, `AggregationRoot`, `IDomainEvent`, `WithErrorCode`, `GuildId`, `MemberId` | всё остальное |
| Domain | `Domain/<Aggregate>/` | агрегаты, Vogen VO, `Policy`, `Errors`, `Events`, `Repositories/IXxx` | Application, Infrastructure, Presentation |
| Application | `Application/` | `UseCases/`, `Dependencies/` (порты), `EventHandlers/` | Infrastructure, Presentation |
| Infrastructure | `Infrastructure/` | Gemini, NetCord-адаптер, EF/pgvector, Hangfire, Wolverine | Presentation |
| Presentation | `Presentation/Discord/` | slash-команды, кнопки, gateway-события | Infrastructure |

Правила закреплены в `.editorconfig` (`DGA0001`, severity=error) и проверяются на каждой сборке. Не отключай правило - перенеси код в правильный слой.

**Prevents:** циклы и протечку Discord/EF/Gemini типов в домен.

## Non-negotiables

1. **Presentation не знает Infrastructure.** Она создаёт команду (record из `Application/UseCases`) и вызывает `bus.InvokeAsync<Result>(command)`. *Prevents:* команды Discord, которые ходят в БД напрямую.
2. **Каждый внешний эффект - порт.** Discord, Gemini, векторный индекс, джобы, UnitOfWork объявляются интерфейсом в `Application/Dependencies/<Area>/` (репозитории - в `Domain/<Agg>/Repositories/`). Реализация - в `Infrastructure`. *Prevents:* use case, которые нельзя тестировать без сети.
3. **Use case = статический класс с суффиксом `UseCase`** и методом `Handle`. Wolverine находит его по суффиксу, зависимости подставляет в параметры. *Prevents:* скрытые конструкторные зависимости и сервис-локатор.
4. **Домен и use case возвращают `FluentResults.Result`.** Исключения - только для багов и недоступной инфраструктуры. Каждая бизнес-ошибка имеет код `Xnnn` (см. `discordmod-domain`). *Prevents:* потерю контекста ошибки и разные формы отказа.
5. **Время и случайность - параметры.** `DateTime now` в `Handle` (Wolverine подставляет значение) или `TimeProvider`. Идентификаторы - `Guid.CreateVersion7()`. *Prevents:* нетестируемые временные правила (лимит «3 бана в час», retention).
6. **Один `UnitOfWork.SaveAsync` на use case**, после всех изменений агрегатов. События домена публикуются автоматически (Wolverine outbox). *Prevents:* частичные записи.
7. **Presentation DTO ≠ доменные типы.** Маппинг в `Presentation/Discord/Mapper` (ручной или Mapperly). *Prevents:* привязку формата команд Discord к домену.
8. **Не смешивать два паттерна в одной фиче.** Вертикальные срезы в папке `Feature/` (как в `notification` care-call) здесь запрещены.
9. **Секреты только через конфиг/user-secrets.** Токен Discord и ключ Gemini не логируются и не коммитятся.

## Раскладка и namespace

Namespace = путь папки: `DiscordGeminiAdmin.Domain.Moderation`, `DiscordGeminiAdmin.Application.UseCases.Moderation.Ban`.
Папка use case: `Application/UseCases/<Module>/<Action>/<Action>UseCase.cs` (команда `record` в том же файле).
Модули: `Guilds`, `Knowledge`, `Moderation`, `Digests`, `Structure`. Новый модуль - новая папка во всех слоях и пункт в `docs/03-folder-structure.md`.

## Именование

| Что | Форма | Пример |
|---|---|---|
| Use case | `<Action>UseCase`, команда `<Action>` | `BanMemberUseCase`, `BanMember` |
| Порт | `I<Capability>` | `IGuildGateway`, `IModerationAi` |
| Репозиторий | `I<Agg>Repository` (домен), `<Agg>Repository` (internal, Infrastructure) | `IModerationCaseRepository` |
| Джоба | `<Action>Job` | `HourlyModerationJob` |
| Slash-модуль | `<Area>Commands` | `ModerationCommands` |
| Кнопки | `<Area>Buttons` | `DigestButtons` |
| Ошибки | `<Agg>Errors` | `ModerationErrors` |

## DI

Каждый слой имеет `DependencyInjection.cs` с C# 14 extension-блоком; подмодули - свои (`AddPersistence`, `AddMessaging`, `AddBackgroundJobs`, ...). `Program.cs` только компонует: `AddApplication`, `AddInfrastructure`, `AddDiscord`.

```csharp
public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddKnowledge(IConfiguration configuration)
        {
            services.AddScoped<IKnowledgeIndex, PgVectorKnowledgeIndex>();
            return services;
        }
    }
}
```

## Wolverine

Включено в `Infrastructure/Messaging/DependencyInjection.cs`: `Discovery.CustomizeHandlerDiscovery(t => t.Includes.WithNameSuffix("UseCase"))`, outbox на Postgres, транзакции EF, ретраи с cooldown. Новый тип use case не требует регистрации - достаточно суффикса.

## Mapperly

`[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)] public static partial class XxxMapper` - как в care-call. Не маппи в домен, маппи из домена в DTO.

## Проверка

```powershell
dotnet build DiscordGeminiAdmin.slnx -warnaserror
dotnet test  DiscordGeminiAdmin.slnx
```

Если `DGA0001` падает - не подавляй: перенеси тип/интерфейс в нужный слой (обычно порт в `Application/Dependencies`).
