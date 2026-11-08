using DiscordGeminiAdmin.SharedKernel;
using FluentResults;

namespace DiscordGeminiAdmin.Domain.Moderation;

public enum ModerationCaseStatus
{
    Open,
    Banned,
    Kicked,
    TimedOut,
    Dismissed,
    Reverted
}

public enum BanSource
{
    Moderator,
    Auto
}

/// <summary>Оценка риска участника, полученная от ИИ и провалидированная.</summary>
public sealed record RiskAssessment(
    Confidence Confidence,
    ModerationReason Reason,
    RuleReference? Rule,
    int EvidenceCount);

/// <summary>Снимок участника на момент решения. Защищённые участники никогда не банятся автоматически.</summary>
public sealed record MemberProfile(bool IsOwner, bool IsBot, bool IsStaff, bool OutranksBot)
{
    public bool IsProtected => IsOwner || IsBot || IsStaff || OutranksBot;
}

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

    public static Error InvalidStatusForDismiss() =>
        new Error("Отклонить можно только открытый кейс").WithErrorCode("M204");

    public static Error InvalidStatusForKick() =>
        new Error("Кикнуть можно только открытый кейс").WithErrorCode("M205");

    public static Error InvalidStatusForTimeout() =>
        new Error("Выдать таймаут можно только по открытому кейсу").WithErrorCode("M206");
}
