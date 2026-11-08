using DiscordGeminiAdmin.SharedKernel;

namespace DiscordGeminiAdmin.Domain.Knowledge.Repositories;

public interface IKnowledgeDocumentRepository
{
    void Add(KnowledgeDocument document);
    Task<KnowledgeDocument?> GetByRuleCodeAsync(GuildId guildId, RuleCode ruleCode, CancellationToken ct);
    Task<IReadOnlySet<string>> GetRuleCodesByGuildAsync(GuildId guildId, CancellationToken ct);
}
