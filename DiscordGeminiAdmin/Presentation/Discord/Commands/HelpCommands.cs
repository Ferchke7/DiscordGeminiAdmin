using DiscordGeminiAdmin.Presentation.Discord.Components;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

public sealed class HelpCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("help", "Интерактивный справочник всех команд и возможностей ИИ-бота")]
    public InteractionMessageProperties Help(
        [SlashCommandParameter(Description = "Раздел справки (overview, admin, mod, kb, organize)")] string section = "overview") =>
        HelpSectionBuilder.BuildMessage(section, ephemeral: true);
}

public sealed class HelpButtons : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("help-tab")]
    public InteractionCallbackProperties SwitchTab(string tab)
    {
        var msg = HelpSectionBuilder.BuildMessage(tab, ephemeral: true);
        return InteractionCallback.ModifyMessage(opts =>
        {
            opts.Embeds = msg.Embeds;
            opts.Components = msg.Components;
        });
    }
}

internal static class HelpSectionBuilder
{
    public static InteractionMessageProperties BuildMessage(string section, bool ephemeral)
    {
        var key = section.Trim().ToLowerInvariant();
        var (title, description, color, fields) = key switch
        {
            "admin" => BuildAdminTab(),
            "mod" => BuildModTab(),
            "kb" => BuildKbTab(),
            "organize" => BuildOrganizeTab(),
            _ => BuildOverviewTab()
        };

        var navRow = new ActionRowProperties([
            new ButtonProperties("help-tab:overview", "🏠 Обзор", key == "overview" || key is not ("admin" or "mod" or "kb" or "organize") ? ButtonStyle.Primary : ButtonStyle.Secondary),
            new ButtonProperties("help-tab:admin", "🔨 Админ (14)", key == "admin" ? ButtonStyle.Primary : ButtonStyle.Secondary),
            new ButtonProperties("help-tab:mod", "🛡️ ИИ-Мод (5)", key == "mod" ? ButtonStyle.Primary : ButtonStyle.Secondary),
            new ButtonProperties("help-tab:kb", "🧠 RAG База (5)", key == "kb" ? ButtonStyle.Primary : ButtonStyle.Secondary),
            new ButtonProperties("help-tab:organize", "🏗️ Сервер (6)", key == "organize" ? ButtonStyle.Primary : ButtonStyle.Secondary)
        ]);

        return EmbedTheme.Card(title, description, color, fields, [navRow], ephemeral);
    }

    private static (string Title, string Description, int Color, EmbedFieldProperties[] Fields) BuildOverviewTab() =>
        (
            "🤖 Центр управления Gemini AI Moderator & Organizer",
            """
            Добро пожаловать! Этот бот сочетает **классическое администрирование Discord**, **почасовой ИИ-аудит сообщений (Gemini 2.5)**, **векторную базу знаний (PostgreSQL + pgvector RAG)** и **умный организатор структуры сервера**.

            Нажмите на кнопки ниже для просмотра подробных команд каждого модуля:
            """,
            EmbedTheme.Primary,
            [
                new EmbedFieldProperties
                {
                    Name = "🚀 Быстрый старт (за 1 минуту)",
                    Value = """
                        1️⃣ `/mod setup` — укажите тематику сервера и канал модераторов.
                        2️⃣ `/kb add` — добавьте правила сервера в векторную базу RAG.
                        3️⃣ `/organize analyze` — получите план улучшения каналов и категорий.
                        """,
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = "🔨 Админ-панель (`/admin`)",
                    Value = "Бан, разбан, кик, таймаут, варны, очистка чата (`purge`), `slowmode`, `lock/unlock`, анонсы и выдача ролей.",
                    Inline = true
                },
                new EmbedFieldProperties
                {
                    Name = "🛡️ ИИ-Модератор (`/mod`)",
                    Value = "Почасовой батч-анализ, защищённый авто-бан с кнопкой разбана, ручной скан участника и ИИ-саммари переписки.",
                    Inline = true
                },
                new EmbedFieldProperties
                {
                    Name = "🧠 RAG & Инфо (`/kb`, `/ask`, `/info`)",
                    Value = "Векторный поиск по правилам, самообучение на решениях модераторов, досье участников и дашборд сервера.",
                    Inline = false
                }
            ]
        );

    private static (string Title, string Description, int Color, EmbedFieldProperties[] Fields) BuildAdminTab() =>
        (
            "🔨 Классические команды администратора (`/admin ...`)",
            "Все команды проверяют иерархию ролей (нельзя наказать владельца или стафф) и автоматически пишут красивый отчёт в мод-канал.",
            EmbedTheme.Danger,
            [
                new EmbedFieldProperties
                {
                    Name = "👤 Модерация участников",
                    Value = """
                        • `/admin ban user reason` — забанить участника (с кнопкой `[Разбанить]` в логах)
                        • `/admin unban user-id reason` — разбанить по Discord ID
                        • `/admin kick user reason` — выгнать участника с сервера
                        • `/admin timeout user minutes reason` — выдать мут (от 1 мин до 28 дней)
                        • `/admin untimeout user reason` — досрочно снять мут
                        • `/admin warn user reason [rule-code]` — выдать официальное предупреждение (страйк в БД)
                        • `/admin warnings user` — посмотреть все страйки и ИИ-кейсы участника
                        """,
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = "💬 Управление каналами и ролями",
                    Value = """
                        • `/admin purge count [user] [reason]` — удалить до 100 сообщений в канале (можно только от 1 юзера)
                        • `/admin slowmode seconds` — задать медленный режим (0 = выкл, до 6 часов)
                        • `/admin lock [reason]` — экстренно закрыть канал для `@everyone` (`🔒`)
                        • `/admin unlock [reason]` — открыть канал обратно (`🔓`)
                        • `/admin announce channel title message [style] [role]` — выпустить красивый Embed-анонс
                        • `/admin role-add user role` / `/admin role-remove user role` — выдать или снять роль
                        """,
                    Inline = false
                }
            ]
        );

    private static (string Title, string Description, int Color, EmbedFieldProperties[] Fields) BuildModTab() =>
        (
            "🛡️ ИИ-Модератор и Автоматическая защита (`/mod ...`)",
            "Каждые 60 минут бот собирает буфер сообщений и проверяет их через **Gemini 2.5 Flash** с опорой на правила из RAG.",
            EmbedTheme.Warning,
            [
                new EmbedFieldProperties
                {
                    Name = "⚙️ Команды управления ИИ",
                    Value = """
                        • `/mod setup theme mod-channel [dry-run]` — настроить тематику сервера и канал модераторов
                        • `/mod settings [min-confidence] [min-evidence] [max-per-hour] [dry-run]` — изменить пороги авто-бана
                        • `/mod scan user` — мгновенная ИИ-проверка участника с кнопками `[Бан] [Кик] [Таймаут] [Игнор]`
                        • `/mod digest-now` — запустить часовой ИИ-прогон прямо сейчас
                        • `/mod summarize [hours]` — сделать ИИ-выжимку (саммари) последних сообщений в текущем канале
                        """,
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = "🔒 5 уровней защиты от ложного авто-бана (`ModerationPolicy`)",
                    Value = """
                        1. **Защита стаффа (`M101`)**: бот никогда не банит владельца, админов и роли выше себя.
                        2. **Порог уверенности (`M102`)**: по умолчанию `>= 95%` и минимум `2` сообщения-доказательства.
                        3. **Проверка правила RAG (`M103`)**: код правила из ответа ИИ обязан существовать в БД сервера.
                        4. **Часовой лимит (`M104`)**: не более `3` авто-банов в час (остальные идут в дайджест на ручную проверку).
                        5. **Dry-Run режим (`M105`)**: тестовый режим + кнопка `[🔓 Разбанить]` на каждой карточке.
                        """,
                    Inline = false
                }
            ]
        );

    private static (string Title, string Description, int Color, EmbedFieldProperties[] Fields) BuildKbTab() =>
        (
            "🧠 Векторная база знаний RAG (`/kb ...` и `/ask`)",
            "Хранит правила, FAQ, описания каналов и прецеденты модерации в **PostgreSQL (`pgvector` `vector(768)`)**.",
            EmbedTheme.Purple,
            [
                new EmbedFieldProperties
                {
                    Name = "📚 Команды базы знаний (для администрации)",
                    Value = """
                        • `/kb add rule-code title content` — добавить или обновить правило/FAQ (с дедупликацией по SHA-256)
                        • `/kb list` — показать все проиндексированные правила, разделы и прецеденты сервера
                        • `/kb delete rule-code` — удалить правило и его векторы из базы знаний
                        • `/kb reindex` — автоматически проиндексировать все категории и топики каналов сервера
                        """,
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = "💬 Команда для всех участников",
                    Value = """
                        • `/ask question` — задать любой вопрос по правилам, ролям или каналам сервера. ИИ найдёт релевантные чанки в `pgvector` и даст точный ответ со ссылками на коды правил!
                        """,
                    Inline = false
                }
            ]
        );

    private static (string Title, string Description, int Color, EmbedFieldProperties[] Fields) BuildOrganizeTab() =>
        (
            "🏗️ ИИ-Организатор структуры и Аналитика (`/organize`, `/info`)",
            "Помогает развивать Discord-сервер под его тематику и отслеживать статистику в реальном времени.",
            EmbedTheme.Cyan,
            [
                new EmbedFieldProperties
                {
                    Name = "🏗️ Организация каналов и категорий (`/organize`)",
                    Value = """
                        • `/organize analyze [goal]` — ИИ (`gemini-2.5-pro`) изучит текущие каналы и роли сервера и пришлёт план улучшений в мод-канал с кнопками `[✅ Одобрить и применить]` и `[✖️ Отклонить]`.
                        • `/organize create-channel name [category] [topic] [is-voice]` — мгновенно создать текстовый или голосовой канал (и категорию, если её ещё нет).
                        """,
                    Inline = false
                },
                new EmbedFieldProperties
                {
                    Name = "📊 Информация и Дашборд (`/info`)",
                    Value = """
                        • `/info status` — статус бота, аптайм, модели Gemini и текущая политика безопасности сервера
                        • `/info server` — полная сводка по серверу: каналы, категории, роли, правила RAG и кейсы
                        • `/info user [user]` — карточка участника: уровень доступа, иерархия и история модерации
                        • `/info cases [limit]` — журнал последних ИИ-кейсов и действий модераторов на сервере
                        """,
                    Inline = false
                }
            ]
        );
}
