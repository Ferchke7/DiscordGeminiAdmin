using DiscordGeminiAdmin.Application.UseCases.Structure.Analyze;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.SharedKernel;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Commands;

[SlashCommand("organize", "ИИ-организатор структуры Discord-сервера по тематике", DefaultGuildPermissions = Permissions.ManageGuild)]
public sealed class OrganizeCommands(IMessageBus bus) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("analyze", "Проанализировать сервер и предложить план улучшения каналов/категорий/ролей")]
    public async Task AnalyzeAsync(
        [SlashCommandParameter(Description = "Дополнительное пожелание к структуре (необязательно)")] string? goal = null)
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

        var result = await bus.InvokeAsync<Result<StructureProposal>>(new AnalyzeGuildStructure(
            GuildId.From(Context.Guild.Id),
            goal));

        if (result.IsFailed)
        {
            await FollowupAsync(new InteractionMessageProperties
            {
                Content = string.Join("; ", result.Errors.Select(e => e.Message)),
                Flags = MessageFlags.Ephemeral
            });
            return;
        }

        await FollowupAsync(new InteractionMessageProperties
        {
            Content = $"План структуры `{result.Value.Id}` ({result.Value.Changes.Count} шагов) отправлен в мод-канал на одобрение.",
            Flags = MessageFlags.Ephemeral
        });
    }
}
