# 04. Вызов модератора (30+ команд, кнопки, расписание) и конфигурация

## 1. Slash-команды Discord

Все ответы бота оформлены в виде стилизованных Discord Embed-карточек (`EmbedTheme`) с цветовой кодировкой, бейджами и интерактивными кнопками.

### 🏠 Общие команды и Интерактивная помощь (`/help`, `/ask`)
| Команда | Параметры | Доступ | Назначение |
|---|---|---|---|
| `/help` | `[section]` (`overview`, `admin`, `mod`, `kb`, `organize`) | Все участники | Интерактивный центр справки с 5 кнопками-вкладками |
| `/ask` | `question` | Все участники | Вопрос ИИ-помощнику по правилам, каналам и FAQ сервера (через `pgvector` RAG) |

### 🔨 Классические команды администратора (`/admin ...`)
| Команда | Параметры | Use Case | Назначение |
|---|---|---|---|
| `/admin ban` | `user`, `[reason]` | `AdminModerationUseCase` | Ручной бан участника с защитой стаффа (`M101`) и кнопкой `[🔓 Разбанить]` в логах |
| `/admin unban` | `user_id`, `[reason]` | `AdminModerationUseCase` | Разбан пользователя по Discord ID с записью в мод-канал |
| `/admin kick` | `user`, `[reason]` | `AdminModerationUseCase` | Исключение (кик) участника с сервера с проверкой иерархии ролей |
| `/admin timeout` | `user`, `[minutes]`, `[reason]` | `AdminModerationUseCase` | Выдача мута (таймаута) от 1 минуты до 28 дней |
| `/admin untimeout` | `user`, `[reason]` | `AdminModerationUseCase` | Досрочное снятие таймаута с участника |
| `/admin warn` | `user`, `reason`, `[rule_code]` | `AdminModerationUseCase` | Выдача официального предупреждения (страйка) с подсчётом истории нарушений |
| `/admin warnings` | `user` | `AdminModerationUseCase` | Просмотр всех страйков, банов и ИИ-кейсов участника |
| `/admin purge` | `[count]`, `[author]`, `[reason]` | `ChannelAdminUseCase` | Массовое удаление от 1 до 100 сообщений в канале (опционально — только одного автора) |
| `/admin slowmode` | `[seconds]`, `[reason]` | `ChannelAdminUseCase` | Установка медленного режима в канале (`0` = выкл, до `21600` сек) |
| `/admin lock` | `[reason]` | `ChannelAdminUseCase` | Экстренная блокировка канала (`🔒`) для `@everyone` во время рейдов/споров |
| `/admin unlock` | `[reason]` | `ChannelAdminUseCase` | Разблокировка канала (`🔓`) для общения |
| `/admin announce` | `channel`, `title`, `message`, `[style]`, `[mention_role]` | `ChannelAdminUseCase` | Публикация красивого объявления (`info`, `warning`, `event`, `rules`) с пингом роли |
| `/admin role-add` | `user`, `role`, `[reason]` | `ChannelAdminUseCase` | Выдача роли участнику с записью в журнал модерации |
| `/admin role-remove` | `user`, `role`, `[reason]` | `ChannelAdminUseCase` | Снятие роли с участника с записью в журнал модерации |

### 🛡️ ИИ-Модератор (`/mod ...`)
| Команда | Параметры | Use Case | Назначение |
|---|---|---|---|
| `/mod setup` | `theme`, `mod_channel`, `[dry_run]` | `SetupGuildUseCase` | Первичная настройка тематики сервера, канала отчётов и режима авто-бана |
| `/mod settings` | `[min_confidence]`, `[min_evidence]`, `[max_per_hour]`, `[dry_run]` | `UpdateGuildSettingsUseCase` | Тонкая настройка порогов `ModerationPolicy` |
| `/mod scan` | `user` | `ScanMemberUseCase` | Внеочередная ИИ-проверка участника с кнопками `[🔨 Бан] [👢 Кик] [🔇 Таймаут] [✅ Игнор]` |
| `/mod digest-now` | — | `RunHourlyModerationUseCase` | Принудительный запуск часового ИИ-анализа всех новых сообщений |
| `/mod summarize` | `[hours]` | `ChannelAdminUseCase` | ИИ-выжимка (саммари) обсуждений в текущем канале за последние `1..48` часов |

### 🧠 База знаний RAG (`/kb ...`)
| Команда | Параметры | Use Case | Назначение |
|---|---|---|---|
| `/kb add` | `rule_code`, `title`, `content` | `IndexDocumentUseCase` | Добавить или обновить правило/раздел в векторной БД `pgvector` |
| `/kb list` | — | `ManageKnowledgeUseCase` | Показать таблицу всех правил, гидов по каналам и прецедентов в RAG |
| `/kb delete` | `rule_code` | `ManageKnowledgeUseCase` | Удалить правило и его векторные чанки из базы знаний |
| `/kb reindex` | — | `ReindexGuildUseCase` | Автоматически проиндексировать все категории и описания каналов сервера |

### 🏗️ ИИ-Организатор и Дашборд (`/organize ...`, `/info ...`)
| Команда | Параметры | Use Case | Назначение |
|---|---|---|---|
| `/organize analyze` | `[goal]` | `AnalyzeGuildStructureUseCase` | ИИ-анализ структуры сервера с карточкой плана и кнопками `[✅ Одобрить]` / `[✖️ Отклонить]` |
| `/organize create-channel` | `name`, `[category]`, `[topic]`, `[is_voice]` | `ChannelAdminUseCase` | Быстрое создание текстового/голосового канала (и категории, если её нет) |
| `/info status` | — | `AdminModerationUseCase` | Диагностика бота: аптайм, RAM, модели Gemini, статус RAG и `ModerationPolicy` |
| `/info server` | — | `AdminModerationUseCase` | Сводная аналитика сервера: каналы, категории, роли, правила и кейсы |
| `/info user` | `[user]` | `AdminModerationUseCase` | Досье участника: статус защиты (`M101`), роли и статистика нарушений |
| `/info cases` | `[limit]` | `AdminModerationUseCase` | Журнал последних кейсов модерации на сервере |

---

## 2. Интерактивные кнопки (Component Interactions)

| Кнопка | `custom_id` | Где появляется | Действие |
|---|---|---|---|
| **Вкладки `/help`** | `help-tab:<section>` | `/help` | Переключает разделы интерактивного справочника без создания новых сообщений |
| **🔨 Бан** | `ban:<caseId>` | Часовой дайджест / `/mod scan` | Банит нарушителя и записывает прецедент в RAG |
| **👢 Кик** | `kick:<caseId>` | Часовой дайджест / `/mod scan` | Кикает участника с сервера |
| **🔇 Таймаут 1ч** | `timeout:<caseId>` | Часовой дайджест / `/mod scan` | Выдаёт мут на 1 час |
| **✅ Игнор** | `dismiss:<caseId>` | Часовой дайджест / `/mod scan` | Отклоняет кейс и обучает RAG не реагировать на подобные ложные срабатывания |
| **🔓 Разбанить** | `unban:<caseId>` | Карточка авто-бана / ручного бана | Снимает бан в Discord и сохраняет прецедент отмены в RAG |
| **✅ Одобрить и применить** | `proposal-approve:<proposalId>` | План `/organize analyze` | Создаёт/обновляет категории, каналы и роли в Discord по утверждённому плану |
| **✖️ Отклонить** | `proposal-reject:<proposalId>` | План `/organize analyze` | Отклоняет план изменений структуры |
