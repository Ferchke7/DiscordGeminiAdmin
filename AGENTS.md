# Инструкции для AI-агента (DiscordGeminiAdmin)

Любую задачу в этом репозитории начинай с чтения скила **`discordmod`**:
- `.agents/skills/discordmod/SKILL.md` — роутер по скилам и главным архитектурным инвариантам.
- `.agents/skills/discordmod-architecture/SKILL.md` — слои (`Domain`, `Application`, `Infrastructure`, `Presentation`), направление зависимостей, анализатор `DGA0001`.
- `.agents/skills/discordmod-domain/SKILL.md` — агрегаты, Vogen VO, `ModerationPolicy`, коды ошибок, тесты.
- `.agents/skills/discordmod-feature/SKILL.md` — вертикальный чек-лист фичи и эталонный срез (`resources/reference-slice.md`).
- `.agents/skills/discordmod-discord/SKILL.md` — NetCord, slash-команды, кнопки, лимиты Discord API.
- `.agents/skills/discordmod-ai/SKILL.md` — Gemini (`Google.GenAI`), structured output, защита от prompt injection.
- `.agents/skills/discordmod-rag/SKILL.md` — PostgreSQL + `pgvector`, чанкинг, изоляция по `GuildId`.

Документация проекта:
- `docs/01-architecture.md` — чистая архитектура и конвенции (на базе `care-call1` `develop`).
- `docs/02-moderator-features.md` — логика и фичи ИИ-модератора и организатора сервера.
- `docs/03-folder-structure.md` — структура папок и модулей.
- `docs/04-invocation-and-config.md` — команды Discord, кнопки, расписание и конфигурация.
