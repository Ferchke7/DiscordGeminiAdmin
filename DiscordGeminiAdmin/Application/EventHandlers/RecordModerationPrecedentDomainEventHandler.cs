using DiscordGeminiAdmin.Application.UseCases.Knowledge.IndexDocument;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Moderation.Events;

namespace DiscordGeminiAdmin.Application.EventHandlers;

/// <summary>
/// Преобразует решения модераторов (бан, отклонение кейса, разбан) в команды индексации прецедентов RAG.
/// Так ИИ-модератор учится на реальных решениях команды сервера.
/// </summary>
public static class RecordModerationPrecedentDomainEventHandler
{
    public static IndexDocument Handle(MemberBannedDomainEvent domainEvent) =>
        new(
            domainEvent.GuildId,
            RuleCode: $"precedent:ban:{domainEvent.CaseId:N}"[..36],
            SourceType: KnowledgeSourceType.Precedent,
            Title: $"Прецедент бана ({domainEvent.Source})",
            Content: $"Подтверждённое нарушение (правило: {domainEvent.RuleCode ?? "не указано"}): {domainEvent.Reason}");

    public static IndexDocument Handle(ModerationCaseDismissedDomainEvent domainEvent) =>
        new(
            domainEvent.GuildId,
            RuleCode: $"precedent:dismiss:{domainEvent.CaseId:N}"[..36],
            SourceType: KnowledgeSourceType.Precedent,
            Title: "Прецедент ложного срабатывания (отклонено модератором)",
            Content: $"НЕ является поводом для наказания (правило: {domainEvent.RuleCode ?? "не указано"}): {domainEvent.OriginalReason}");

    public static IndexDocument Handle(BanRevertedDomainEvent domainEvent) =>
        new(
            domainEvent.GuildId,
            RuleCode: $"precedent:revert:{domainEvent.CaseId:N}"[..36],
            SourceType: KnowledgeSourceType.Precedent,
            Title: "Прецедент отменённого бана (разбанено модератором)",
            Content: $"Ошибочный бан отменён модератором (правило: {domainEvent.RuleCode ?? "не указано"}): {domainEvent.OriginalReason}");
}
