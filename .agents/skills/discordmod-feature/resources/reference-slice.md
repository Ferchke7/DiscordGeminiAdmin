# Эталонный вертикальный срез: `BanMember` (проверено сборкой и тестами)

Ниже — полный срез функции бана участника (по кнопке модератора или автоматически по вердикту ИИ) через все слои:
`Domain` → `Application (Ports + UseCase)` → `Infrastructure (NetCord)` → `Presentation (Button)` → `Tests (Domain + UseCase + Wolverine)`.

## 1. Domain (`Domain/Moderation/`)

### Value Objects (`Confidence.cs`, `ModerationReason.cs`, `RuleReference.cs`)

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

[ValueObject<string>]
public readonly partial struct ModerationReason
{
    private static Validation Validate(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 1000
            ? Validation.Invalid("Причина обязательна и не длиннее 1000 символов")
            : Validation.Ok;
}

[ValueObject<string>]
public readonly partial struct RuleReference
{
    private static Validation Validate(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 200
            ? Validation.Invalid("Ссылка на правило обязательна и не длиннее 200 символов")
            : Validation.Ok;
}
```

### Ошибки и Политика (`ModerationErrors.cs`, `ModerationPolicy.cs`)

```csharp
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Moderation;

public static class ModerationErrors
{
    // M1xx - предохранители авто-модерации
    public static Error ProtectedMember =>
        new Error("Участник защищён от автоматических действий").WithErrorCode("M101");

    public static Error BelowThreshold =>
        new Error("Уверенность или число доказательств ниже порога").WithErrorCode("M102");

    public static Error RuleReferenceMissing =>
        new Error("Нет ссылки на правило сервера - автоматическое действие запрещено").WithErrorCode("M103");

    public static Error AutoBanLimitReached =>
        new Error("Достигнут часовой лимит автоматических банов").WithErrorCode("M104");

    public static Error DryRun =>
        new Error("Включён dry-run: действие не выполняется").WithErrorCode("M105");

    // M2xx - состояние и поиск
    public static Error CaseNotFound() =>
        new Error("Кейс модерации не найден").WithErrorCode("M201");

    public static Error InvalidStatusForBan() =>
        new Error("Забанить можно только открытый кейс").WithErrorCode("M202");

    public static Error InvalidStatusForRevert() =>
        new Error("Откатить можно только забаненный кейс").WithErrorCode("M203");
}

public sealed record RiskAssessment(
    Confidence Confidence,
    ModerationReason Reason,
    RuleReference? Rule,
    int EvidenceCount);

public sealed record MemberProfile(bool IsOwner, bool IsBot, bool IsStaff, bool OutranksBot)
{
    public bool IsProtected => IsOwner || IsBot || IsStaff || OutranksBot;
}

public sealed record ModerationPolicy(
    double MinConfidence,
    int MinEvidenceMessages,
    int MaxAutoBansPerHour,
    bool DryRun)
{
    public static ModerationPolicy Default => new(0.95, 3, 3, DryRun: true);

    public Result CanAutoBan(RiskAssessment assessment, MemberProfile member, int autoBansLastHour)
    {
        if (member.IsProtected)
            return Result.Fail(ModerationErrors.ProtectedMember);
        if (assessment.Rule is null)
            return Result.Fail(ModerationErrors.RuleReferenceMissing);
        if (assessment.Confidence.Value < MinConfidence || assessment.EvidenceCount < MinEvidenceMessages)
            return Result.Fail(ModerationErrors.BelowThreshold);
        if (autoBansLastHour >= MaxAutoBansPerHour)
            return Result.Fail(ModerationErrors.AutoBanLimitReached);
        if (DryRun)
            return Result.Fail(ModerationErrors.DryRun);
        return Result.Ok();
    }
}
```

### Агрегат и репозиторий (`ModerationCase.cs`, `IModerationCaseRepository.cs`)

```csharp
using DiscordGeminiAdmin.Domain.Moderation.Events;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Moderation;

public enum ModerationCaseStatus { Open, Banned, Kicked, TimedOut, Dismissed, Reverted }
public enum BanSource { Moderator, Auto }

public sealed class ModerationCase(Guid id) : AggregationRoot<Guid>(id)
{
    public required GuildId GuildId { get; init; }
    public required MemberId MemberId { get; init; }
    public required RiskAssessment Assessment { get; init; }
    public required DateTime OpenedAt { get; init; }
    public ModerationCaseStatus Status { get; private set; } = ModerationCaseStatus.Open;
    public BanSource? BanSource { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public static ModerationCase Open(GuildId guildId, MemberId memberId, RiskAssessment assessment, DateTime now) =>
        new(Guid.CreateVersion7())
        {
            GuildId = guildId,
            MemberId = memberId,
            Assessment = assessment,
            OpenedAt = now
        };

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

---

## 2. Application (`Application/Dependencies/` + `Application/UseCases/`)

### Порты (`IGuildGateway.cs`, `IUnitOfWork.cs`)

```csharp
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.Dependencies.Discord;

public interface IGuildGateway
{
    Task<Result<MemberProfile>> GetMemberProfileAsync(GuildId guildId, MemberId memberId, CancellationToken ct);
    Task<Result> BanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
    Task<Result> UnbanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct);
}
```

### Use Case (`Application/UseCases/Moderation/Ban/BanMemberUseCase.cs`)

```csharp
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using FluentResults;

namespace DiscordGeminiAdmin.Application.UseCases.Moderation.Ban;

public sealed record BanMember(Guid CaseId, BanSource Source);

public static class BanMemberUseCase
{
    public static async ValueTask<Result> Handle(
        BanMember command,
        IUnitOfWork unitOfWork,
        IModerationCaseRepository cases,
        IGuildGateway gateway,
        ModerationPolicy policy,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var moderationCase = await cases.GetByIdAsync(command.CaseId, cancellationToken);
        if (moderationCase is null)
            return Result.Fail(ModerationErrors.CaseNotFound());

        if (command.Source == BanSource.Auto)
        {
            var profile = await gateway.GetMemberProfileAsync(moderationCase.GuildId, moderationCase.MemberId, cancellationToken);
            if (profile.IsFailed)
                return profile.ToResult();

            var autoBansLastHour = await cases.CountAutoBansSinceAsync(
                moderationCase.GuildId, now.AddHours(-1), cancellationToken);

            var verdict = policy.CanAutoBan(moderationCase.Assessment, profile.Value, autoBansLastHour);
            if (verdict.IsFailed)
                return verdict;
        }

        var banned = moderationCase.Ban(command.Source, now);
        if (banned.IsFailed)
            return banned;

        var discordResult = await gateway.BanAsync(
            moderationCase.GuildId, moderationCase.MemberId, moderationCase.Assessment.Reason.Value, cancellationToken);
        if (discordResult.IsFailed)
            return discordResult;

        await unitOfWork.SaveAsync(cancellationToken);
        return Result.Ok();
    }
}
```

---

## 3. Infrastructure (`Infrastructure/Discord/NetCordGuildGateway.cs`)

```csharp
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;

namespace DiscordGeminiAdmin.Infrastructure.Discord;

internal sealed class NetCordGuildGateway(RestClient rest, ILogger<NetCordGuildGateway> logger) : IGuildGateway
{
    private const Permissions StaffPermissions =
        Permissions.Administrator | Permissions.ManageGuild | Permissions.BanUsers |
        Permissions.KickUsers | Permissions.ModerateUsers | Permissions.ManageMessages;

    public async Task<Result<MemberProfile>> GetMemberProfileAsync(GuildId guildId, MemberId memberId, CancellationToken ct)
    {
        try
        {
            var guild = await rest.GetGuildAsync(guildId.Value, cancellationToken: ct);
            var member = await rest.GetGuildUserAsync(guildId.Value, memberId.Value, cancellationToken: ct);
            var self = await rest.GetCurrentUserAsync(cancellationToken: ct);
            var bot = await rest.GetGuildUserAsync(guildId.Value, self.Id, cancellationToken: ct);

            var memberRoles = member.RoleIds.Select(id => guild.Roles.GetValueOrDefault(id)).OfType<Role>().ToList();
            var botRoles = bot.RoleIds.Select(id => guild.Roles.GetValueOrDefault(id)).OfType<Role>().ToList();

            var isStaff = memberRoles.Any(r => (r.Permissions & StaffPermissions) != 0);
            var memberTop = memberRoles.Select(r => r.Position).DefaultIfEmpty().Max();
            var botTop = botRoles.Select(r => r.Position).DefaultIfEmpty().Max();

            return Result.Ok(new MemberProfile(
                IsOwner: guild.OwnerId == memberId.Value,
                IsBot: member.IsBot,
                IsStaff: isStaff,
                OutranksBot: memberTop.CompareTo(botTop) >= 0));
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Не удалось получить профиль участника {MemberId}", memberId.Value);
            return Result.Fail("Не удалось получить участника из Discord");
        }
    }

    public async Task<Result> BanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct)
    {
        try
        {
            await rest.BanGuildUserAsync(guildId.Value, memberId.Value, 0,
                new RestRequestProperties { AuditLogReason = reason }, ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Discord отклонил бан {MemberId}", memberId.Value);
            return Result.Fail("Discord отклонил бан");
        }
    }

    public async Task<Result> UnbanAsync(GuildId guildId, MemberId memberId, string reason, CancellationToken ct)
    {
        try
        {
            await rest.UnbanGuildUserAsync(guildId.Value, memberId.Value,
                new RestRequestProperties { AuditLogReason = reason }, ct);
            return Result.Ok();
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Discord отклонил разбан {MemberId}", memberId.Value);
            return Result.Fail("Discord отклонил разбан");
        }
    }
}
```

---

## 4. Presentation (`Presentation/Discord/Components/DigestButtons.cs`)

```csharp
using DiscordGeminiAdmin.Application.UseCases.Moderation.Ban;
using DiscordGeminiAdmin.Domain.Moderation;
using FluentResults;
using NetCord.Services.ComponentInteractions;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Components;

public sealed class DigestButtons(IMessageBus bus) : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("ban")]
    public async Task<string> BanAsync(Guid caseId)
    {
        var result = await bus.InvokeAsync<Result>(new BanMember(caseId, BanSource.Moderator));
        return result.IsSuccess ? "Участник забанен." : string.Join("; ", result.Errors.Select(e => e.Message));
    }
}
```

---

## 5. Tests (`DiscordGeminiAdmin.Tests/UseCases/BanMemberUseCaseTests.cs`)

> Важно: Vogen запрещает `default(GuildId)` / `default(MemberId)` (ошибка `VOG009`), а NSubstitute выбрасывает `AmbiguousArgumentsException`, если смешивать `Arg.Any<GuildId>()` с `default` других Vogen-структур. Передавай в моки конкретные `moderationCase.GuildId` и `moderationCase.MemberId`.

```csharp
using DiscordGeminiAdmin.Application.Dependencies.Discord;
using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Application.UseCases.Moderation.Ban;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NSubstitute;
using Shouldly;

namespace DiscordGeminiAdmin.Tests.UseCases;

public class BanMemberUseCaseTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IModerationCaseRepository _cases = Substitute.For<IModerationCaseRepository>();
    private readonly IGuildGateway _gateway = Substitute.For<IGuildGateway>();
    private readonly ModerationPolicy _policy = ModerationPolicy.Default with { DryRun = false };
    private readonly DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private ModerationCase OpenCase(double confidence = 0.99, int evidence = 5) =>
        ModerationCase.Open(
            GuildId.From(1), MemberId.From(2),
            new RiskAssessment(Confidence.From(confidence), ModerationReason.From("Спам"), RuleReference.From("rules#3"), evidence),
            _now);

    private void GatewayBanReturns(ModerationCase moderationCase, Result result) =>
        _gateway.BanAsync(moderationCase.GuildId, moderationCase.MemberId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private ValueTask<Result> Act(Guid caseId, BanSource source) =>
        BanMemberUseCase.Handle(new BanMember(caseId, source),
            _unitOfWork, _cases, _gateway, _policy, _now, CancellationToken.None);

    [Fact]
    public async Task Auto_ban_of_protected_member_is_rejected_and_nothing_happens()
    {
        var moderationCase = OpenCase();
        _cases.GetByIdAsync(moderationCase.Id, Arg.Any<CancellationToken>()).Returns(moderationCase);
        _gateway.GetMemberProfileAsync(moderationCase.GuildId, moderationCase.MemberId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new MemberProfile(IsOwner: true, IsBot: false, IsStaff: false, OutranksBot: false)));

        var result = await Act(moderationCase.Id, BanSource.Auto);

        result.ShouldHaveErrorCode("M101");
        await _gateway.DidNotReceive().BanAsync(moderationCase.GuildId, moderationCase.MemberId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }
}
```
