using DiscordGeminiAdmin.Domain.Digests;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Application.Dependencies.Discord;

/// <summary>Порт публикации отчётов, карточек аудита и предложений структуры в мод-канал.</summary>
public interface IDiscordNotifier
{
    Task<Result> SendAutoBanAuditCardAsync(
        ChannelId modChannelId,
        ModerationCase moderationCase,
        CancellationToken ct);

    Task<Result> SendHourlyDigestAsync(
        ChannelId modChannelId,
        HourlyDigest digest,
        CancellationToken ct);

    Task<Result> SendStructureProposalAsync(
        ChannelId modChannelId,
        StructureProposal proposal,
        CancellationToken ct);

    Task<Result> SendAdminActionLogAsync(
        ChannelId modChannelId,
        string actionTitle,
        MemberId? targetMemberId,
        MemberId moderatorId,
        string reason,
        uint colorHex,
        Guid? unbanCaseId,
        CancellationToken ct);
}
