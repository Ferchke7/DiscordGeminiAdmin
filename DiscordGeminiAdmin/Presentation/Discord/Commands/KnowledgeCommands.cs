using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.Ask;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.IndexDocument;
using DiscordGeminiAdmin.Application.UseCases.Knowledge.ReindexGuild;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

[SlashCommand("kb", "Управление RAG-базой знаний сервера", DefaultGuildPermissions = Permissions.ManageGuild)]
public sealed class KnowledgeCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("add", "Добавить или обновить правило/раздел в RAG-базе сервера")]
    public async Task AddRuleAsync(
        [SlashCommandParameter(Description = "Код правила (например: rules#1 или faq#voice)")] string ruleCode,
        [SlashCommandParameter(Description = "Заголовок правила или раздела")] string title,
        [SlashCommandParameter(Description = "Текст правила или раздела")] string content)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(Ephemeral("Команда доступна только на сервере.")));
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
            await FollowupAsync(Ephemeral(string.Join("; ", result.Errors.Select(e => e.Message))));
            return;
        }

        var r = result.Value;
        var msg = r.WasSkippedUnchanged
            ? $"Документ `{ruleCode}` не изменился (ContentHash совпал) — переиндексация не потребовалась."
            : $"Документ `{ruleCode}` проиндексирован в RAG (чанков: `{r.ChunksIndexed}`).";
        await FollowupAsync(Ephemeral(msg));
    }

    [SubSlashCommand("reindex", "Переиндексировать описания каналов и тематику сервера в RAG")]
    public async Task ReindexAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(Ephemeral("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<IndexDocumentResult>>(
            new ReindexGuild(GuildId.From(Context.Guild.Id)));

        if (result.IsFailed)
        {
            await FollowupAsync(Ephemeral(string.Join("; ", result.Errors.Select(e => e.Message))));
            return;
        }

        var r = result.Value;
        await FollowupAsync(Ephemeral(r.WasSkippedUnchanged
            ? "Структура каналов не изменилась с прошлой индексации."
            : $"Структура каналов проиндексирована (чанков: `{r.ChunksIndexed}`)."));
    }

    private static InteractionMessageProperties Ephemeral(string text) =>
        new() { Content = text, Flags = MessageFlags.Ephemeral };
}

public sealed class AskCommand(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ask", "Задать вопрос по правилам и тематике сервера (RAG)")]
    public async Task AskAsync(
        [SlashCommandParameter(Description = "Ваш вопрос")] string question)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties
            {
                Content = "Команда доступна только на сервере.",
                Flags = MessageFlags.Ephemeral
            }));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<KnowledgeAnswerDto>>(new AskKnowledge(
            GuildId.From(Context.Guild.Id),
            question));

        if (result.IsFailed)
        {
            await FollowupAsync(new InteractionMessageProperties
            {
                Content = string.Join("; ", result.Errors.Select(e => e.Message)),
                Flags = MessageFlags.Ephemeral
            });
            return;
        }

        var cited = result.Value.CitedRuleCodes.Count > 0
            ? $"\n\n_Источники: `{string.Join("`, `", result.Value.CitedRuleCodes)}`_"
            : string.Empty;

        await FollowupAsync(new InteractionMessageProperties
        {
            Content = $"{result.Value.Answer}{cited}",
            Flags = MessageFlags.Ephemeral
        });
    }
}
