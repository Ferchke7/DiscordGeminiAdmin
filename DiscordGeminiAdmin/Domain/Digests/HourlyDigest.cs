using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Digests;

public static class DigestErrors
{
    public static Error EmptyDigest =>
        new Error("В дайджесте должен быть хотя бы один кейс").WithErrorCode("D101");

    public static Error NotFound() =>
        new Error("Часовой дайджест не найден").WithErrorCode("D201");
}

public sealed record HourlyDigestItem(
    Guid CaseId,
    MemberId MemberId,
    Confidence Confidence,
    string Reason,
    string? RuleCode,
    bool WasDryRunAutoBan);

/// <summary>Часовой отчёт модерации, публикуемый в мод-канале с кнопками действий.</summary>
public sealed class HourlyDigest(Guid id) : AggregationRoot<Guid>(id)
{
    public required GuildId GuildId { get; init; }
    public required DateTime PeriodStart { get; init; }
    public required DateTime PeriodEnd { get; init; }
    public required IReadOnlyList<HourlyDigestItem> Items { get; init; }
    public required DateTime CreatedAt { get; init; }

    public static Result<HourlyDigest> Create(
        GuildId guildId,
        DateTime periodStart,
        DateTime periodEnd,
        IReadOnlyList<HourlyDigestItem> items,
        DateTime now)
    {
        if (items.Count == 0)
            return Result.Fail(DigestErrors.EmptyDigest);

        return new HourlyDigest(Guid.CreateVersion7())
        {
            GuildId = guildId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Items = items,
            CreatedAt = now
        };
    }
}
