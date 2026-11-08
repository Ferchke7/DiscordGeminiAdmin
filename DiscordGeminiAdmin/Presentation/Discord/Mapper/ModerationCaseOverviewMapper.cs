using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.SharedKernel;
using Riok.Mapperly.Abstractions;

namespace DiscordGeminiAdmin.Presentation.Discord.Mapper;

public sealed record ModerationCaseOverviewDto(
    Guid Id,
    GuildId GuildId,
    MemberId MemberId,
    RiskAssessment Assessment,
    ModerationCaseStatus Status);

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class ModerationCaseOverviewMapper
{
    public static partial ModerationCaseOverviewDto ToDto(ModerationCase src);
}
