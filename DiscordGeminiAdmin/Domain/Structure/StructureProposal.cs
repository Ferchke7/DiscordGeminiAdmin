using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Structure;

public enum ProposedChangeKind
{
    CreateCategory,
    CreateTextChannel,
    CreateVoiceChannel,
    UpdateChannelTopic,
    RenameChannel,
    CreateRole
}

public enum ProposalStatus
{
    Pending,
    Approved,
    Applied,
    Rejected,
    Failed
}

public sealed record ProposedChange(
    ProposedChangeKind Kind,
    string TargetName,
    string? ParentCategory,
    string? NewNameOrTopic,
    string Rationale);

public static class StructureErrors
{
    public static Error EmptyProposal =>
        new Error("План улучшения структуры не содержит ни одного шага").WithErrorCode("S101");

    public static Error ProposalNotFound() =>
        new Error("План улучшения структуры не найден").WithErrorCode("S201");

    public static Error InvalidStatusForApprove() =>
        new Error("Одобрить можно только ожидающий план").WithErrorCode("S202");

    public static Error InvalidStatusForReject() =>
        new Error("Отклонить можно только ожидающий план").WithErrorCode("S203");
}

/// <summary>Предложение ИИ по улучшению каналов/категорий/ролей сервера по его тематике.</summary>
public sealed class StructureProposal(Guid id) : AggregationRoot<Guid>(id)
{
    public required GuildId GuildId { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<ProposedChange> Changes { get; init; }
    public required DateTime CreatedAt { get; init; }
    public ProposalStatus Status { get; private set; } = ProposalStatus.Pending;
    public DateTime? ResolvedAt { get; private set; }

    public static Result<StructureProposal> Create(
        GuildId guildId,
        string summary,
        IReadOnlyList<ProposedChange> changes,
        DateTime now)
    {
        if (changes.Count == 0)
            return Result.Fail(StructureErrors.EmptyProposal);

        return new StructureProposal(Guid.CreateVersion7())
        {
            GuildId = guildId,
            Summary = summary.Trim(),
            Changes = changes,
            CreatedAt = now
        };
    }

    public Result Approve(DateTime now)
    {
        if (Status != ProposalStatus.Pending)
            return Result.Fail(StructureErrors.InvalidStatusForApprove());

        Status = ProposalStatus.Approved;
        ResolvedAt = now;
        return Result.Ok();
    }

    public void MarkApplied(DateTime now)
    {
        Status = ProposalStatus.Applied;
        ResolvedAt = now;
    }

    public void MarkFailed(DateTime now)
    {
        Status = ProposalStatus.Failed;
        ResolvedAt = now;
    }

    public Result Reject(DateTime now)
    {
        if (Status != ProposalStatus.Pending)
            return Result.Fail(StructureErrors.InvalidStatusForReject());

        Status = ProposalStatus.Rejected;
        ResolvedAt = now;
        return Result.Ok();
    }
}
