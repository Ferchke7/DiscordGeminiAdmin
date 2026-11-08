using DiscordGeminiAdmin.Application.UseCases.Moderation.ChannelActions;
using DiscordGeminiAdmin.Application.UseCases.Structure.Analyze;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Presentation.Discord.Components;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

[SlashCommand("organize", "ИИ-организатор структуры Discord-сервера и каналов по тематике", DefaultGuildPermissions = Permissions.ManageGuild)]
public sealed class OrganizeCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("analyze", "Проанализировать сервер через ИИ и предложить план улучшения каналов/категорий/ролей")]
    public async Task AnalyzeAsync(
        [SlashCommandParameter(Description = "Дополнительное пожелание к структуре (необязательно)")] string? goal = null)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<StructureProposal>>(new AnalyzeGuildStructure(
            GuildId.From(Context.Guild.Id),
            goal));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка анализа структуры"));
            return;
        }

        var p = result.Value;
        await FollowupAsync(EmbedTheme.SuccessCard(
            "План реорганизации сформирован ИИ",
            $"Карточка с планом отправлена в канал модераторов. Нажмите **`[✅ Одобрить и применить]`**, чтобы бот автоматически создал категории, каналы и роли.",
            [
                new EmbedFieldProperties { Name = "🆔 ID Плана", Value = $"`{p.Id}`", Inline = true },
                new EmbedFieldProperties { Name = "📋 Шагов в плане", Value = $"`{p.Changes.Count}`", Inline = true },
                new EmbedFieldProperties { Name = "📝 Краткое резюме", Value = p.Summary.Length > 300 ? p.Summary[..300] + "..." : p.Summary, Inline = false }
            ]));
    }

    [SubSlashCommand("create-channel", "Быстро создать новый текстовый или голосовой канал в нужной категории")]
    public async Task CreateChannelAsync(
        [SlashCommandParameter(Description = "Название нового канала (например: новости-проекта)")] string name,
        [SlashCommandParameter(Description = "Название категории (если нет — бот создаст её автоматически)")] string? category = null,
        [SlashCommandParameter(Description = "Описание (топик) канала")] string? topic = null,
        [SlashCommandParameter(Description = "Голосовой канал? (false = текстовый, true = голосовой)")] bool isVoice = false)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(InteractionCallback.Message(EmbedTheme.ErrorCard("Команда доступна только на сервере.")));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result<ChannelId>>(new CreateServerChannel(
            GuildId.From(Context.Guild.Id),
            MemberId.From(Context.User.Id),
            name,
            category,
            topic,
            isVoice));

        if (result.IsFailed)
        {
            await FollowupAsync(EmbedTheme.ErrorCard(result, "Ошибка создания канала"));
            return;
        }

        await FollowupAsync(EmbedTheme.SuccessCard(
            "Канал создан",
            $"Создан {(isVoice ? "🔊 голосовой" : "💬 текстовый")} канал <#{result.Value.Value}>{(string.IsNullOrWhiteSpace(category) ? string.Empty : $" в категории **`{category}`**")}."));
    }
}
