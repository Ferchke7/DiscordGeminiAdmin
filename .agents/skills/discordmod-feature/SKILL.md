---
name: discordmod-feature
description: >-
  Use when adding, changing or reviewing a complete feature slice in DiscordGeminiAdmin: a new use case with its Domain model, port, Infrastructure adapter, Discord command or button, DI registration, config keys and tests (for example a new moderation action, a knowledge command, a structure change type). Covers the vertical checklist and the verified reference slice (ban a member). Do NOT use for pure layer questions (discordmod-architecture), aggregate internals (discordmod-domain), NetCord details (discordmod-discord), Gemini prompts (discordmod-ai) or pgvector (discordmod-rag).
---

# discordmod-feature

Фича = **срез сверху вниз по слоям**. Не смешивай с vertical-slice-папками (`Feature/…` в `notification` care-call) - здесь один паттерн.

## Порядок работы (всегда в этом порядке)

1. **Прочитай** `discordmod-architecture` и `docs/02-moderator-features.md`: фича уже описана? Если нет - допиши описание сначала.
2. **Domain.** Агрегат/метод/VO/Policy/ошибки/события. Тесты домена. (`discordmod-domain`)
3. **Application.** Команда (`record`) + `XxxUseCase.Handle`. Новые внешние зависимости - порты в `Application/Dependencies/<Area>/`. Тесты use case с `Substitute.For<>()`.
4. **Infrastructure.** Реализация порта (`internal sealed class`), регистрация в `DependencyInjection.cs` соответствующей папки. EF-конфигурация и миграция для новых сущностей.
5. **Presentation.** Slash-команда или кнопка: только разбор ввода → `bus.InvokeAsync<Result>(command)` → текст ответа. (`discordmod-discord`)
6. **Конфиг и docs.** Новые ключи в `appsettings.json` и `docs/04-invocation-and-config.md`; новые коды ошибок в каталог.
7. **Проверка:** `dotnet build DiscordGeminiAdmin.slnx -warnaserror` и `dotnet test DiscordGeminiAdmin.slnx`.

## Non-negotiables

1. **Use case не знает NetCord/Gemini/EF.** Только порты. *Prevents:* нетестируемую логику.
2. **Эффекты в Discord - после доменной проверки, до `SaveAsync`.** Порядок: загрузить агрегат → доменный метод (`Result`) → `IGuildGateway` → `unitOfWork.SaveAsync`. Отказ Discord → ничего не сохраняем. *Prevents:* «забанен в БД, но не в Discord».
3. **Каждый код ошибки use case покрыт тестом**, и на пути отказа проверено: `SaveAsync` и внешний порт не вызваны. *Prevents:* тихие регрессии предохранителей.
4. **Авто-действие ИИ идёт через `ModerationPolicy`**, ручное решение модератора - нет (человек принимает решение). Различай `BanSource.Auto`/`BanSource.Moderator`. *Prevents:* обход предохранителей.
5. **Presentation ничего не решает.** Ни порогов, ни проверок прав доменного уровня; права Discord-команды (`default_member_permissions`) - да. *Prevents:* бизнес-логику в обработчиках.
6. **Ответ пользователю - краткий, на русском, без внутренних деталей** (стектрейсы, id моделей, промпты). Ephemeral для служебных команд.
7. **Логи без содержимого сообщений и токенов.** Логируй идентификаторы кейсов и коды ошибок.

## Чек-лист «готово»

- [ ] Новый тип в нужной папке, namespace = путь.
- [ ] `DGA0001` не срабатывает (сборка зелёная).
- [ ] Тест на успех + на каждый код ошибки + «ничего не вызвано при отказе».
- [ ] Новые порты зарегистрированы в `DependencyInjection.cs`.
- [ ] Для новой таблицы: `IEntityTypeConfiguration`, миграция (`dotnet ef migrations add ...`), `docs/03-folder-structure.md`.
- [ ] Для новой команды: описана в `docs/04-invocation-and-config.md`.
- [ ] Для фичи, использующей ИИ: JSON-схема ответа, валидация, тест на невалидный ответ (`discordmod-ai`).

## Эталонный срез

`resources/reference-slice.md` - **проверенный сборкой и тестами** срез «забанить участника» (Domain → UseCase → IGuildGateway → NetCord-адаптер → кнопка → тесты, включая проверку Wolverine-обнаружения `*UseCase`). Копируй форму, а не имена.

## Типовые ловушки

| Ловушка | Решение |
|---|---|
| `default(GuildId)` не компилируется (Vogen VOG009) | Создавай `GuildId.From(1)`; в NSubstitute используй конкретные значения вместо `Arg.Any<GuildId>()` (иначе `AmbiguousArgumentsException`) |
| `Wolverine` не находит use case | Имя класса должно заканчиваться на `UseCase`, класс `public static`, метод `Handle` |
| `DateTime now` в сигнатуре | Wolverine подставляет текущее время сам; в тестах передавай фиксированное |
| Команда Discord отвечает > 3 секунд | `DeferAsync` сначала, затем `FollowupAsync` (см. `discordmod-discord`) |
| Хочется `DateTime.UtcNow` в домене | Параметр `now` |
