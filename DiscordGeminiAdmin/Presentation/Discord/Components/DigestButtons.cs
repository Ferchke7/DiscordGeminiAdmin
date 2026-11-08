using DiscordGeminiAdmin.Application.UseCases.Moderation.Ban;
using DiscordGeminiAdmin.Application.UseCases.Moderation.Dismiss;
using DiscordGeminiAdmin.Application.UseCases.Moderation.Kick;
using DiscordGeminiAdmin.Application.UseCases.Moderation.RevertBan;
using DiscordGeminiAdmin.Application.UseCases.Moderation.Timeout;
using DiscordGeminiAdmin.Domain.Moderation;
using FluentResults;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using Wolverine;

namespace DiscordGeminiAdmin.Presentation.Discord.Components;

/// <summary>Обработчики кнопок часового дайджеста, сканирования и карточки авто-бана.</summary>
public sealed class DigestButtons(IMessageBus bus) : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("ban")]
    public async Task<InteractionMessageProperties> BanAsync(Guid caseId)
    {
        var result = await bus.InvokeAsync<Result>(new BanMember(caseId, BanSource.Moderator));
        return Reply(result, "Участник забанен", "Кейс закрыт, а прецедент нарушения сохранён в векторную базу RAG.");
    }

    [ComponentInteraction("kick")]
    public async Task<InteractionMessageProperties> KickAsync(Guid caseId)
    {
        var result = await bus.InvokeAsync<Result>(new KickMember(caseId));
        return Reply(result, "Участник кикнут", "Участник исключён с сервера по решению модератора.");
    }

    [ComponentInteraction("timeout")]
    public async Task<InteractionMessageProperties> TimeoutAsync(Guid caseId)
    {
        var result = await bus.InvokeAsync<Result>(new TimeoutMember(caseId, TimeSpan.FromHours(1)));
        return Reply(result, "Выдан таймаут на 1 час", "Участник временно переведён в режим чтения.");
    }

    [ComponentInteraction("dismiss")]
    public async Task<InteractionMessageProperties> DismissAsync(Guid caseId)
    {
        var result = await bus.InvokeAsync<Result>(new DismissCase(caseId));
        return Reply(result, "Кейс отклонён (Игнор)", "Ложное срабатывание записано в RAG — ИИ учтёт это при следующих проверках.");
    }

    [ComponentInteraction("unban")]
    public async Task<InteractionMessageProperties> UnbanAsync(Guid caseId)
    {
        var result = await bus.InvokeAsync<Result>(new RevertBan(caseId));
        return Reply(result, "Бан снят", "Пользователь разблокирован, а отмена авто-бана сохранена в прецеденты RAG.");
    }

    private static InteractionMessageProperties Reply(Result result, string title, string okText) =>
        result.IsSuccess
            ? EmbedTheme.SuccessCard(title, okText)
            : EmbedTheme.ErrorCard(result, title);
}
