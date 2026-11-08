using DiscordGeminiAdmin.Application.UseCases.Structure.Approve;
using DiscordGeminiAdmin.Application.UseCases.Structure.Reject;
using FluentResults;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Components;

/// <summary>Кнопки одобрения или отклонения плана улучшения структуры сервера.</summary>
public sealed class StructureProposalButtons(IMessageBus bus) : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("proposal-approve")]
    public async Task ApproveAsync(Guid proposalId)
    {
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var result = await bus.InvokeAsync<Result>(new ApproveProposal(proposalId));
        await FollowupAsync(result.IsSuccess
            ? EmbedTheme.SuccessCard(
                "План структуры применён",
                "Категории, каналы и роли сервера успешно обновлены по утверждённому плану.")
            : EmbedTheme.ErrorCard(result, "Ошибка применения плана"));
    }

    [ComponentInteraction("proposal-reject")]
    public async Task<InteractionMessageProperties> RejectAsync(Guid proposalId)
    {
        var result = await bus.InvokeAsync<Result>(new RejectProposal(proposalId));
        return result.IsSuccess
            ? EmbedTheme.Card("✖️ План структуры отклонён", "Изменения не были применены к серверу.", EmbedTheme.Warning)
            : EmbedTheme.ErrorCard(result, "Ошибка отклонения плана");
    }
}
