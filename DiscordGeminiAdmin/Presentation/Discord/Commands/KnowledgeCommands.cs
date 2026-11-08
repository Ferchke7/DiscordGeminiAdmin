using System.Text;
using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.Ask;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.IndexDocument;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.Manage;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.ReindexGuild;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

[SlashCommand("kb", "Управление векторной RAG-базой знаний сервера (pgvector)", DefaultGuildPermissions = Permissions.ManageGuild)]
public sealed class KnowledgeCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("add", "Добавить или обновить правило/раздел в RAG-базе сервера")]
    public async Task AddRuleAsync(
        [SlashCommandParameter(Description = "Код правила (например: R1, rules#spam или faq#voice)")] string ruleCode,
        [SlashCommandParameter(Description = "Заголовок правила или раздела")] string title,
        [SlashCommandParameter(Description = "Текст правила или раздела")] string content)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<IndexDocumentResult>>(new IndexDocument(
            GuildId.From(Context.Guild.Id),
            ruleCode,
            KnowledgeSourceType.Rules,
            title,
            content));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка индексации правила"));
            return;
        }

        var r = result.Value;
        await FollowupAsync(EmbedTheme.SuccessCard(
            r.WasSkippedUnchanged ? "Документ не изменился" : "Правило проиндексировано в RAG",
            r.WasSkippedUnchanged
                ? $"Правило `{ruleCode}` уже актуально в базе знаний (SHA-256 совпал)."
                : $"Документ **`{ruleCode}` — {title}** разбит на чанки и сохранён в `pgvector`.",
            [
                new EmbedFieldProperties { Name = "🏷️ Код правила", Value = $"`{ruleCode}`", Inline = true },
                new EmbedFieldProperties { Name = "🧩 Векторных чанков", Value = $"`{r.ChunksIndexed}`", Inline = true }
            ]));
    }

    [SubSlashCommand("list", "Показать все правила, FAQ и прецеденты в RAG-базе сервера")]
    public async Task ListAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<IReadOnlyList<KnowledgeDocument>>>(
            new ListKnowledgeDocuments(GuildId.From(Context.Guild.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result));
            return;
        }

        if (result.Value.Count == 0)
        {
            await FollowupAsync(EmbedTheme.Card(
                "📚 База знаний сервера пока пуста",
                "Добавьте первое правило командой `/kb add` или проиндексируйте структуру каналов через `/kb reindex`.",
                EmbedTheme.Warning));
            return;
        }

        var sb = new StringBuilder();
        foreach (var doc in result.Value.Take(20))
        {
            var icon = doc.SourceType switch
            {
                KnowledgeSourceType.Rules => "📜",
                KnowledgeSourceType.ChannelGuide => "💬",
                KnowledgeSourceType.ThemeAndFaq => "❓",
                KnowledgeSourceType.Precedent => "⚖️",
                _ => "📄"
            };
            sb.AppendLine($"{icon} **`{doc.RuleCode.Value}`** — **{doc.Title}** _({doc.SourceType}, обновлён {doc.UpdatedAt:dd.MM HH:mm})_");
        }

        await FollowupAsync(EmbedTheme.Card(
            $"🧠 Документы в RAG-базе знаний ({result.Value.Count})",
            sb.ToString(),
            EmbedTheme.Purple));
    }

    [SubSlashCommand("delete", "Удалить правило или документ из RAG-базы знаний сервера")]
    public async Task DeleteAsync(
        [SlashCommandParameter(Description = "Код правила для удаления (например: R1)")] string ruleCode)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new DeleteKnowledgeDocument(
            GuildId.From(Context.Guild.Id),
            ruleCode));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Не удалось удалить документ"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Документ удалён из RAG",
            $"Правило `{ruleCode}` и все его векторные чанки удалены из базы знаний сервера."));
    }

    [SubSlashCommand("reindex", "Переиндексировать описания каналов и тематику сервера в RAG")]
    public async Task ReindexAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<IndexDocumentResult>>(
            new ReindexGuild(GuildId.From(Context.Guild.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка переиндексации"));
            return;
        }

        var r = result.Value;
        await FollowupAsync(EmbedTheme.SuccessCard(
            r.WasSkippedUnchanged ? "Структура каналов актуальна" : "Структура каналов проиндексирована",
            r.WasSkippedUnchanged
                ? "Категории и описания каналов не изменились с прошлой индексации."
                : $"Описания всех каналов сервера сохранены в векторный индекс (чанков: **`{r.ChunksIndexed}`**)."));
    }
}

public sealed class AskCommand(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ask", "Задать вопрос ИИ-помощнику по правилам и каналам сервера (RAG)")]
    public async Task AskAsync(
        [SlashCommandParameter(Description = "Ваш вопрос по серверу или правилам")] string question)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<KnowledgeAnswerDto>>(new AskKnowledge(
            GuildId.From(Context.Guild.Id),
            question));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка обращения к базе знаний"));
            return;
        }

        var sources = result.Value.CitedRuleCodes.Count > 0
            ? string.Join(", ", result.Value.CitedRuleCodes.Select(c => $"`{c}`"))
            : "`общий контекст сервера`";

        await FollowupAsync(EmbedTheme.Card(
            "🧠 Ответ ИИ по базе знаний сервера",
            result.Value.Answer,
            EmbedTheme.Purple,
            [
                new EmbedFieldProperties { Name = "❓ Ваш вопрос", Value = question, Inline = false },
                new EmbedFieldProperties { Name = "📜 Источники (RAG)", Value = sources, Inline = false }
            ]));
    }
}
