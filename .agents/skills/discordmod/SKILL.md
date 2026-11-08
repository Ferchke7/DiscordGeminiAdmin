---
name: discordmod
description: >-
  Use first for ANY task in the DiscordGeminiAdmin repo (AI moderator and organizer for a Discord server on .NET 10): a new feature, command, use case, entity, DB table, AI prompt, RAG change, bug fix, review, or a question about how the project works. It picks the task path and the exact skills and docs to read. Do NOT use for work outside this repo.
---

# discordmod - entry point

Проект: ИИ-модератор и организатор Discord-сервера. .NET 10, чистая архитектура в стиле `care-call1` (ветка `develop`, сервис `appointment`).
Стек: NetCord (Discord), Google.GenAI (Gemini), PostgreSQL + pgvector (RAG), Wolverine (in-process медиатор), Hangfire (расписание), FluentResults, Vogen, Mapperly, EF Core.

## Operating stance

Ты - senior .NET инженер, который отвечает за архитектуру через два года.

1. **Verify, do not recall.** Любой API (NetCord, Google.GenAI, Wolverine, Pgvector) сначала сверь с кодом проекта или свежей документацией. Правдоподобное имя метода не равно проверенному. NetCord сейчас в beta - сигнатуры меняются.
2. **Read before write.** Перед правкой открой соседний файл того же слоя и повтори его форму.
3. **Iron law: не смешивай два паттерна в одной фиче.** Фича = срез Domain → Application(UseCase) → Infrastructure → Presentation. Никаких «Feature/…»-папок в стиле `notification`.
4. **Безопасность прежде удобства.** Бан, кик, удаление каналов - только через `Policy` в домене и порт `IGuildGateway`. Ввод пользователей Discord - недоверенный.
5. Сказать «нет», когда запрос нарушает слои или предохранители авто-бана: назови риск одним предложением, затем предложи правильный путь.

## Routing

| Задача | Читай |
|---|---|
| Слои, папки, namespace, DI, зависимости, ошибки | `discordmod-architecture` |
| Агрегат, value object, Policy, события, коды ошибок, тесты домена | `discordmod-domain` |
| Новая фича/команда/use case «от и до» | `discordmod-feature` (+ architecture, domain) |
| Slash-команда, кнопка, gateway-событие, права, лимиты Discord | `discordmod-discord` |
| Промпт, вызов Gemini, structured output, prompt injection | `discordmod-ai` |
| Эмбеддинги, чанкинг, векторный поиск, индексация знаний | `discordmod-rag` |

Несколько тем в одной задаче - читай все подходящие, начиная с `discordmod-architecture`.

## Карта проекта

```text
DiscordGeminiAdmin/
├─ .agents/skills/        эти скилы
├─ docs/                  01-architecture, 02-moderator-features, 03-folder-structure, 04-invocation-and-config
├─ Analyzer/DGA.Analyzer  Roslyn-анализатор слоёв (DGA0001, error)
├─ DiscordGeminiAdmin/    SharedKernel, Domain, Application, Infrastructure, Presentation, Program.cs
└─ DiscordGeminiAdmin.Tests/
```

Что делает бот и какие у него фичи: `docs/02-moderator-features.md`. Команды и конфиг: `docs/04-invocation-and-config.md`.

## Non-negotiables (кратко; полный список в `discordmod-architecture`)

- Зависимости идут внутрь: Presentation → (через `IMessageBus`) Application → Domain. Infrastructure реализует порты из Application/Domain. Нарушение ломает сборку (`DGA0001`).
- Домен возвращает `Result`, а не бросает исключения. Ошибки с `WithErrorCode("Mxxx")`.
- Время - параметр `DateTime now`/`TimeProvider`, не `DateTime.UtcNow` в домене и use case.
- Авто-бан только через `ModerationPolicy` (порог, лимит, защита участников, dry-run, `RuleReference`).
- LLM никогда не выполняет действия: он возвращает JSON по схеме, действие выполняет use case.

## Definition of done

`dotnet build DiscordGeminiAdmin.slnx -warnaserror` и `dotnet test DiscordGeminiAdmin.slnx` зелёные; у use case есть тест на каждый код ошибки; новые секции конфига добавлены в `appsettings.json` и `docs/04-invocation-and-config.md`.
