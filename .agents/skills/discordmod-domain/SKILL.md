---
name: discordmod-domain
description: >-
  Use when designing or changing the Domain layer of DiscordGeminiAdmin: aggregates (GuildProfile, KnowledgeDocument, ModerationCase, HourlyDigest, StructureProposal), Vogen value objects, ModerationPolicy and auto-ban safeguards, domain events, error code catalog (Gxxx, Kxxx, Mxxx, Sxxx, Dxxx), repository interfaces, and unit tests of domain and use cases. Do NOT use for Discord commands (discordmod-discord), Gemini prompts (discordmod-ai), vector search (discordmod-rag) or layer wiring (discordmod-architecture).
---

# discordmod-domain

Эталон формы: `appointment/.../Domain/Appointments/Appointment.cs`, `AppointmentErrors.cs`, `AppointmentPolicy.cs` в `C:\care-call1\services`.

## Non-negotiables

1. **Агрегат создаётся фабрикой, меняется методами.** `Create/Open(...)` возвращает `Result<T>` (или `T`, если инварианты уже гарантированы Vogen). Публичные сеттеры состояния - `private set`. *Prevents:* обход инвариантов из use case.
2. **Бизнес-отказ = `Result.Fail(Error с кодом)`, не исключение.** `DomainException` - только для нарушенных предусловий (баг). *Prevents:* потерю кода ошибки.
3. **Примитивы с правилами - Vogen value object** (`[ValueObject<double>] readonly partial struct`): `Confidence`, `ModerationReason`, `RuleReference`, `GuildId`, `MemberId`. Нельзя `default(VO)` - Vogen запрещает (VOG009), это защита. *Prevents:* «магические» double/ulong/string.
4. **Домен чист:** ни `NetCord`, ни `Google.GenAI`, ни `EF`, ни `Wolverine`. Время приходит параметром `DateTime now`. *Prevents:* зависимость правил от внешнего мира.
5. **Правила авто-действий живут в `ModerationPolicy`**, а не в use case и не в промпте. Любой новый предохранитель = новый код ошибки + тест на него. *Prevents:* ослабление защиты «по месту».
6. **События домена** - `record : IDomainEvent`, добавляются через `AddDomainEvent` внутри метода агрегата; обработчики в `Application/EventHandlers`. *Prevents:* побочные эффекты в агрегате.
7. **ID агрегатов - `Guid.CreateVersion7()`**; идентификаторы Discord - `GuildId`/`MemberId` (ulong).
8. **Репозиторий - интерфейс в `Domain/<Agg>/Repositories`**, методы возвращают агрегаты и скаляры, а не `IQueryable`. Тяжёлые чтения (дайджесты, списки) - отдельный query-порт в `Application/Dependencies`.

## Агрегаты и статусы

| Агрегат | Ключевое поведение | Статусы |
|---|---|---|
| `GuildProfile` | тематика, язык, мод-канал, защищённые роли, настройки AutoBan | - |
| `KnowledgeDocument` (+`KnowledgeChunk`) | контент, `ContentHash`, переиндексация | `Pending → Indexed → Stale` |
| `ModerationCase` | `Open`, `Ban`, `Kick`, `Timeout`, `Dismiss`, `Revert` | `Open → Banned/Kicked/TimedOut/Dismissed`, `Banned → Reverted` |
| `HourlyDigest` | набор `ModerationCase`, публикация | `Draft → Published` |
| `StructureProposal` (+`ProposedChange`) | `Approve`, `Reject`, `MarkApplied`, `MarkFailed` | `Pending → Approved → Applied/Failed`, `Pending → Rejected` |

## Каталог кодов ошибок

| Префикс | Область | Примеры |
|---|---|---|
| `G1xx/G2xx` | Guilds | `G201 GuildNotConfigured` |
| `K1xx/K2xx` | Knowledge | `K101 DocumentTooLarge`, `K201 DocumentNotFound` |
| `M1xx` | Предохранители модерации | `M101 ProtectedMember`, `M102 BelowThreshold`, `M103 RuleReferenceMissing`, `M104 AutoBanLimitReached`, `M105 DryRun` |
| `M2xx` | Moderation: состояние | `M201 CaseNotFound`, `M202 InvalidStatusForBan`, `M203 InvalidStatusForRevert` |
| `S1xx/S2xx` | Structure | `S101 EmptyProposal`, `S201 ProposalNotFound`, `S202 InvalidStatusForApprove` |
| `D1xx/D2xx` | Digests | `D201 DigestNotFound` |

Код уникален во всём проекте. Новый код добавь в этот каталог и в `docs/02-moderator-features.md`.

## Шаблон: value object

```csharp
using Vogen;

namespace DiscordGeminiAdmin.Domain.Moderation;

[ValueObject<double>]
public readonly partial struct Confidence
{
    private static Validation Validate(double value) =>
        double.IsNaN(value) || value is < 0 or > 1
            ? Validation.Invalid("Уверенность должна быть в диапазоне 0..1")
            : Validation.Ok;
}
```

## Шаблон: ошибки

```csharp
public static class ModerationErrors
{
    public static Error ProtectedMember =>
        new Error("Участник защищён от автоматических действий").WithErrorCode("M101");

    public static Error CaseNotFound() =>
        new Error("Кейс модерации не найден").WithErrorCode("M201");
}
```

## Шаблон: policy (предохранители авто-бана)

Порядок проверок важен: защита участника → ссылка на правило → порог → лимит → dry-run.

```csharp
public sealed record ModerationPolicy(double MinConfidence, int MinEvidenceMessages, int MaxAutoBansPerHour, bool DryRun)
{
    public static ModerationPolicy Default => new(0.95, 3, 3, DryRun: true);

    public Result CanAutoBan(RiskAssessment assessment, MemberProfile member, int autoBansLastHour)
    {
        if (member.IsProtected) return Result.Fail(ModerationErrors.ProtectedMember);
        if (assessment.Rule is null) return Result.Fail(ModerationErrors.RuleReferenceMissing);
        if (assessment.Confidence.Value < MinConfidence || assessment.EvidenceCount < MinEvidenceMessages)
            return Result.Fail(ModerationErrors.BelowThreshold);
        if (autoBansLastHour >= MaxAutoBansPerHour) return Result.Fail(ModerationErrors.AutoBanLimitReached);
        if (DryRun) return Result.Fail(ModerationErrors.DryRun);
        return Result.Ok();
    }
}
```

`DryRun` возвращается как `M105`: use case трактует его как «записать в аудит, что бот забанил бы», а не как сбой.

## Шаблон: агрегат

```csharp
public sealed class ModerationCase(Guid id) : AggregationRoot<Guid>(id)
{
    public required GuildId GuildId { get; init; }
    public required MemberId MemberId { get; init; }
    public required RiskAssessment Assessment { get; init; }
    public required DateTime OpenedAt { get; init; }
    public ModerationCaseStatus Status { get; private set; } = ModerationCaseStatus.Open;
    public BanSource? BanSource { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public Result Ban(BanSource source, DateTime now)
    {
        if (Status != ModerationCaseStatus.Open)
            return Result.Fail(ModerationErrors.InvalidStatusForBan());

        Status = ModerationCaseStatus.Banned;
        BanSource = source;
        ResolvedAt = now;
        AddDomainEvent(new MemberBannedDomainEvent(Id, GuildId, MemberId, Assessment.Reason.Value, source));
        return Result.Ok();
    }
}
```

Полный проверенный срез (Domain + UseCase + gateway + кнопка + тесты) лежит в `discordmod-feature/resources/reference-slice.md`.

## Тесты

xUnit + NSubstitute + Shouldly, как в care-call. Хелпер `ShouldHaveErrorCode("M101")` уже есть в `DiscordGeminiAdmin.Tests/ResultAssertions.cs`.

- Домен: каждый код ошибки агрегата/policy - отдельный `[Fact]`; граничные значения (ровно порог проходит, на единицу ниже - нет).
- Use case: вызывай `XxxUseCase.Handle(...)` напрямую с `Substitute.For<>()`; проверяй, что при отказе не вызван `IUnitOfWork.SaveAsync` и порт `IGuildGateway`.
- Для VO в `Arg.Any<>` используй `Arg.Any<GuildId>()` - `default` запрещён Vogen.
- Время фиксируй: `new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc)`.
- Сложное создание агрегата - Builder в `DiscordGeminiAdmin.Tests` (как `AppointmentBuilder`).
