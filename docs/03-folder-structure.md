# 03. Структура папок и модулей проекта

Дерево папок уже создано в репозитории. Каждый бизнес-модуль (`Guilds`, `Knowledge`, `Moderation`, `Digests`, `Structure`) зеркалируется в `Domain`, `Application/UseCases` и `Infrastructure/Persistence`.

```text
DiscordGeminiAdmin/
├─ .agents/
│  └─ skills/                              # Скилы для AI-агента (Progressive Disclosure)
│     ├─ discordmod/                       # Роутер (точка входа для любой задачи)
│     ├─ discordmod-architecture/          # Слои, правила зависимостей, DGA0001, DI, Wolverine
│     ├─ discordmod-domain/                # Агрегаты, Vogen VO, ModerationPolicy, коды ошибок
│     ├─ discordmod-feature/               # Чек-лист вертикального среза + resources/reference-slice.md
│     ├─ discordmod-discord/               # NetCord: интенты, slash-команды, кнопки, лимиты
│     ├─ discordmod-ai/                    # Google.GenAI: JSON-схемы, защита от prompt injection
│     └─ discordmod-rag/                   # PostgreSQL + pgvector, чанкинг, изоляция по GuildId
│
├─ Analyzer/
│  ├─ DGA.Analyzer/                        # Roslyn-анализатор чистой архитектуры (правило DGA0001)
│  └─ DGA.Analyzer.Tests/                  # Юнит-тесты анализатора
│
├─ DiscordGeminiAdmin/                     # Основной хост-проект (Microsoft.NET.Sdk.Worker, net10.0)
│  ├─ SharedKernel/                        # Entity, AggregationRoot, IDomainEvent, ErrorExtensions, GuildId, MemberId
│  ├─ Domain/                              # Чистая бизнес-логика (0 внешних зависимостей)
│  │  ├─ Guilds/                           # GuildProfile, настройки тематики и авто-бана
│  │  ├─ Knowledge/                        # KnowledgeDocument, KnowledgeChunk, ContentHash
│  │  ├─ Moderation/                       # ModerationCase, ModerationPolicy, RiskAssessment, ModerationErrors
│  │  ├─ Digests/                          # HourlyDigest
│  │  └─ Structure/                        # StructureProposal, ProposedChange
│  │
│  ├─ Application/                         # Сценарии использования (оркестрация)
│  │  ├─ Dependencies/                     # Порты (интерфейсы для Infrastructure)
│  │  │  ├─ Ai/                            # IModerationAi, IStructureAdvisorAi
│  │  │  ├─ Discord/                       # IGuildGateway, IDiscordNotifier
│  │  │  ├─ Knowledge/                     # IEmbeddingService, IKnowledgeIndex
│  │  │  ├─ Persistence/                   # IUnitOfWork, query-сервисы
│  │  │  └─ BackgroundJobs/                # IModerationBackgroundTasks
│  │  ├─ UseCases/                         # Статические классы *UseCase + команды record
│  │  │  ├─ Guilds/                        # SetupGuild, UpdateGuildSettings
│  │  │  ├─ Knowledge/                     # IndexDocument, ReindexGuild, AskKnowledge
│  │  │  ├─ Moderation/                    # IngestMessage, RunHourlyModeration, BanMember, RevertBan, DismissCase
│  │  │  ├─ Digests/                       # PublishHourlyDigest
│  │  │  └─ Structure/                     # AnalyzeGuildStructure, ApproveProposal, RejectProposal
│  │  ├─ EventHandlers/                    # Обработчики IDomainEvent (запись прецедентов в RAG, уведомления)
│  │  └─ DependencyInjection.cs            # AddApplication()
│  │
│  ├─ Infrastructure/                      # Адаптеры внешних систем
│  │  ├─ Ai/Gemini/                        # GeminiModerationAi, GeminiStructureAdvisorAi, GeminiEmbeddingService
│  │  ├─ Discord/                          # NetCordGuildGateway, DiscordNotifier
│  │  ├─ Knowledge/                        # PgVectorKnowledgeIndex, MarkdownChunker
│  │  ├─ Persistence/                      # DatabaseContext, UnitOfWork, DatabaseMigration, репозитории и EF-конфигурации
│  │  ├─ BackgroundJobs/                   # Hangfire: HourlyModerationJob, RetentionCleanupJob
│  │  ├─ Messaging/                        # Wolverine: обнаружение *UseCase, транзакции, outbox
│  │  └─ DependencyInjection.cs            # AddInfrastructure()
│  │
│  ├─ Presentation/
│  │  └─ Discord/                          # Входящие события и команды Discord (NetCord)
│  │     ├─ Commands/                      # Slash-команды (/mod, /kb, /ask, /organize)
│  │     ├─ Components/                    # Кнопки дайджеста, разбана и одобрения структуры
│  │     ├─ Events/                        # MessageCreateGatewayHandler (буферизация сообщений)
│  │     ├─ Mapper/                        # Маппинг запросов/ответов (Mapperly)
│  │     └─ DependencyInjection.cs         # AddDiscord(), UseDiscord()
│  │
│  ├─ appsettings.json
│  └─ Program.cs                           # Точка входа Generic Host
│
├─ DiscordGeminiAdmin.Tests/               # Тесты Domain и UseCases (xUnit, NSubstitute, Shouldly)
│  ├─ Domain/
│  ├─ UseCases/
│  └─ ResultAssertions.cs                  # ShouldHaveErrorCode(...)
│
├─ docs/                                   # Архитектурная и функциональная документация
├─ .editorconfig                           # Правила слоёв DGA0001 и AsyncFixer
├─ Directory.Build.props                   # Общие настройки сборки и анализаторов
├─ Directory.Packages.props                # Central Package Management (единые версии пакетов)
├─ docker-compose.yml                      # PostgreSQL 17 + pgvector
└─ DiscordGeminiAdmin.slnx                 # Файл решения
```
