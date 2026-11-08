using DiscordGeminiAdmin.Presentation.Discord.Components;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace DiscordGeminiAdmin.Presentation.Discord.Events;

/// <summary>
/// Перехватывает любые ошибки выполнения slash-команд и кнопок (включая исключения после DeferredMessage)
/// и выводит в чат наглядную карточку с объяснением причины ошибки.
/// </summary>
public sealed class DiscordErrorResultHandler :
    IApplicationCommandResultHandler<ApplicationCommandContext>,
    IComponentInteractionResultHandler<ButtonInteractionContext>
{
    public async ValueTask HandleResultAsync(
        IExecutionResult result,
        ApplicationCommandContext context,
        GatewayClient? client,
        ILogger logger,
        IServiceProvider services)
    {
        if (result is not IFailResult failResult)
            return;

        var card = BuildFailureCard(
            failResult,
            $"Ошибка команды /{context.Interaction.Data.Name}",
            logger);

        await SendErrorSafelyAsync(context.Interaction, context.Channel.Id, client, card, logger);
    }

    public async ValueTask HandleResultAsync(
        IExecutionResult result,
        ButtonInteractionContext context,
        GatewayClient? client,
        ILogger logger,
        IServiceProvider services)
    {
        if (result is not IFailResult failResult)
            return;

        var card = BuildFailureCard(
            failResult,
            $"Ошибка обработки кнопки ({context.Interaction.Data.CustomId})",
            logger);

        await SendErrorSafelyAsync(context.Interaction, context.Channel.Id, client, card, logger);
    }

    private static InteractionMessageProperties BuildFailureCard(
        IFailResult failResult,
        string title,
        ILogger logger)
    {
        if (failResult is IExceptionResult exResult)
        {
            logger.LogError(exResult.Exception, "{Title}: {Message}", title, failResult.Message);
            return EmbedTheme.ExceptionCard(exResult.Exception, title);
        }

        logger.LogWarning("{Title}: {Message}", title, failResult.Message);
        return EmbedTheme.ErrorCard(failResult.Message, title);
    }

    private static async ValueTask SendErrorSafelyAsync(
        Interaction interaction,
        ulong channelId,
        GatewayClient? client,
        InteractionMessageProperties card,
        ILogger logger)
    {
        try
        {
            await interaction.SendResponseAsync(InteractionCallback.Message(card));
            return;
        }
        catch (RestException)
        {
            // Интеракция уже была подтверждена через DeferredMessage — отправляем Followup
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SendResponseAsync не удался, пробуем SendFollowupMessageAsync");
        }

        try
        {
            await interaction.SendFollowupMessageAsync(card);
            return;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SendFollowupMessageAsync не удался, отправляем сообщение напрямую в канал {ChannelId}", channelId);
        }

        if (client is not null)
        {
            try
            {
                await client.Rest.SendMessageAsync(channelId, EmbedTheme.ToChannelMessage(card));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось отправить карточку ошибки в канал {ChannelId}", channelId);
            }
        }
    }
}
