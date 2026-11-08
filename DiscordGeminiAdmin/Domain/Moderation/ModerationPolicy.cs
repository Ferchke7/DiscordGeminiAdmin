using FluentResults;

namespace DiscordGeminiAdmin.Domain.Moderation;

/// <summary>Правила, по которым действие ИИ допускается без человека. Значения приходят из конфига или GuildProfile.</summary>
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
