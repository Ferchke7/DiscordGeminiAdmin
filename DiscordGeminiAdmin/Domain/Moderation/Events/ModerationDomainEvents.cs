using DiscordGeminiAdmin.SharedKernel;

namespace DiscordGeminiAdmin.Domain.Moderation.Events;

public sealed record MemberBannedDomainEvent(
    Guid CaseId,
    GuildId GuildId,
    MemberId MemberId,
    string Reason,
    string? RuleCode,
    BanSource Source) : IDomainEvent;

public sealed record ModerationCaseDismissedDomainEvent(
    Guid CaseId,
    GuildId GuildId,
    MemberId MemberId,
    string OriginalReason,
    string? RuleCode) : IDomainEvent;

public sealed record BanRevertedDomainEvent(
    Guid CaseId,
    GuildId GuildId,
    MemberId MemberId,
    string OriginalReason,
    string? RuleCode) : IDomainEvent;
