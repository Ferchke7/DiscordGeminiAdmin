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
        await FollowupAsync(new InteractionMessageProperties
        {
            Content = result.IsSuccess
                ? "План одобрен и успешно применён к каналам/ролям сервера."
                : string.Join("; ", result.Errors.Select(e => e.Message)),
            Flags = MessageFlags.Ephemeral
        });
    }

    [ComponentInteraction("proposal-reject")]
    public async Task<InteractionMessageProperties> RejectAsync(Guid proposalId)
    {
        var result = await bus.InvokeAsync<Result>(new RejectProposal(proposalId));
        return new InteractionMessageProperties
        {
            Content = result.IsSuccess
                ? "План изменения структуры отклонён."
                : string.Join("; ", result.Errors.Select(e => e.Message)),
            Flags = MessageFlags.Ephemeral
        };
    }
}
