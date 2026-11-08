# 04. Вызов модератора (команды, кнопки, расписание) и конфигурация

## 1. Slash-команды Discord

Все административные команды по умолчанию доступны только участникам с правами `ManageGuild` / `BanUsers` и отвечают в `Ephemeral`-режиме (видно только вызвавшему).

| Группа / команда | Параметры | Use Case | Назначение |
|---|---|---|---|
| `/mod setup` | `theme`, `mod_channel`, `dry_run` | `SetupGuildUseCase` | Первичная настройка тематики сервера, канала отчётов и режима авто-бана |
| `/mod settings` | `[min_confidence]`, `[max_per_hour]`, `[dry_run]` | `UpdateGuildSettingsUseCase` | Просмотр или изменение порогов авто-бана |
| `/mod scan` | `user` (`@участник`) | `ScanMemberUseCase` | Внеочередная ИИ-проверка последних сообщений выбранного участника |
| `/mod digest-now` | — | `RunHourlyModerationUseCase` | Принудительный запуск часового анализа прямо сейчас |
| `/kb add` | `title`, `rule_code`, `content` | `IndexDocumentUseCase` | Добавить правило, FAQ или описание тематики в RAG сервера |
| `/kb reindex` | — | `ReindexGuildUseCase` | Переиндексировать описания каналов, ролей и закреплённые правила (по `ContentHash`) |
| `/ask` | `question` | `AskKnowledgeUseCase` | Задать вопрос по правилам/тематике сервера (доступно всем участникам) |
| `/organize analyze` | `[goal]` | `AnalyzeGuildStructureUseCase` | Попросить ИИ предложить улучшение каналов/категорий/ролей под тематику сервера |

---

## 2. Интерактивные кнопки (Component Interactions)

Кнопки прикрепляются к сообщениям бота в мод-канале. Формат `custom_id` — `<action>:<entityGuid>`.

| Кнопка | `custom_id` | Где появляется | Use Case |
|---|---|---|---|
| **Бан** | `ban:<caseId>` | Часовой дайджест | `BanMemberUseCase` (`BanSource.Moderator`) |
| **Кик** | `kick:<caseId>` | Часовой дайджест | `KickMemberUseCase` |
| **Таймаут 1ч** | `timeout:<caseId>` | Часовой дайджест | `TimeoutMemberUseCase` |
| **Игнор** | `dismiss:<caseId>` | Часовой дайджест | `DismissCaseUseCase` (запоминает ложное срабатывание в RAG) |
| **Разбанить** | `unban:<caseId>` | Карточка авто-бана / дайджест | `RevertBanUseCase` (снимает бан в Discord и пишет прецедент в RAG) |
| **Одобрить план** | `proposal-approve:<proposalId>` | План организации сервера | `ApproveProposalUseCase` → применяет изменения в Discord |
| **Отклонить план** | `proposal-reject:<proposalId>` | План организации сервера | `RejectProposalUseCase` |

---

## 3. Фоновые задачи (Hangfire)

| Джоба | Расписание (Cron) | Что делает |
|---|---|---|
| `HourlyModerationJob` | `Digest:Cron` (`0 * * * *` — каждый час) | Для каждой активной гильдии вызывает `RunHourlyModeration` через `IMessageBus` |
| `RetentionCleanupJob` | `15 * * * *` | Удаляет буфер сообщений старше `Retention:MessageBufferHours` (по умолчанию 24 ч) |

---

## 4. Конфигурация (`appsettings.json` и `user-secrets`)

Секреты **никогда** не хранятся в `appsettings.json`. Задавай их через `dotnet user-secrets` или переменные окружения:

```powershell
cd C:\Users\ferda\OneDrive\Desktop\AiGemDiscord\DiscordGeminiAdmin\DiscordGeminiAdmin
dotnet user-secrets set "Discord:Token" "<DISCORD_BOT_TOKEN>"
dotnet user-secrets set "Gemini:ApiKey" "<GEMINI_API_KEY>"
dotnet user-secrets set "Gemini:Models:Moderation" "gemini-2.5-flash"
dotnet user-secrets set "Gemini:Models:Structure" "gemini-2.5-pro"
dotnet user-secrets set "Gemini:Models:Embedding" "text-embedding-004"
```

### Ключи `appsettings.json`

| Ключ | По умолчанию | Описание |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Port=5432;Database=discord_gemini_admin;Username=dga;Password=dga` | Строка подключения к PostgreSQL (`docker-compose up -d`) |
| `Discord:Token` | `""` (из `user-secrets`) | Токен бота Discord |
| `Gemini:ApiKey` | `""` (из `user-secrets`) | API-ключ Gemini |
| `Gemini:Models:Moderation` | `""` | Модель для часового анализа сообщений |
| `Gemini:Models:Structure` | `""` | Модель для генерации плана структуры сервера |
| `Gemini:Models:Embedding` | `""` | Модель эмбеддингов для RAG |
| `Gemini:EmbeddingDimensions` | `768` | Размерность вектора `vector(768)` |
| `AutoBan:Enabled` | `true` | Разрешён ли автоматический бан в принципе |
| `AutoBan:DryRun` | `true` | На старте `true`: бот пишет в отчёт «забанил бы», но не банит в Discord |
| `AutoBan:MinConfidence` | `0.95` | Минимальная уверенность ИИ (`0..1`) для авто-бана |
| `AutoBan:MinEvidenceMessages` | `3` | Минимум сообщений-доказательств для авто-бана |
| `AutoBan:MaxPerHour` | `3` | Максимум авто-банов за 1 час на одну гильдию |
| `Digest:Cron` | `"0 * * * *"` | Cron-выражение часового прогона |
| `Retention:MessageBufferHours` | `24` | Сколько часов хранить буфер сообщений для анализа |

---

## 5. Необходимые настройки бота в Discord Developer Portal

1. В разделе **Bot → Privileged Gateway Intents** включить:
   - `SERVER MEMBERS INTENT` (для проверки ролей и иерархии `MemberProfile`)
   - `MESSAGE CONTENT INTENT` (для чтения текста сообщений в буфер часового анализа)
2. Права бота при приглашении на сервер (`OAuth2 → URL Generator`, scopes: `bot`, `applications.commands`):
   - `View Channels`, `Send Messages`, `Embed Links`, `Read Message History`
   - `Ban Members`, `Kick Members`, `Moderate Members` (для модерации)
   - `Manage Channels`, `Manage Roles` (для применения одобренных планов `StructureProposal`)
