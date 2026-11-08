using System.Collections.Concurrent;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace DiscordGeminiAdmin.Presentation.Discord.Events;

/// <summary>
/// Мгновенно синхронизирует slash-команды для каждой гильдии при старте бота и при добавлении на новый сервер,
/// чтобы команды появлялись в Discord UI без часового ожидания глобального кэша.
/// </summary>
public sealed class GuildCommandSyncGatewayHandler(
    RestClient restClient,
    ApplicationCommandServiceManager commandServiceManager,
    ILogger<GuildCommandSyncGatewayHandler> logger)
    : IReadyGatewayHandler, IGuildCreateGatewayHandler
{
    private readonly ConcurrentDictionary<ulong, byte> _syncedGuilds = new();
    private ulong _applicationId;

    public async ValueTask HandleAsync(ReadyEventArgs arg)
    {
        _applicationId = arg.User.Id;

        try
        {
            // Очищаем старые глобальные команды, чтобы не было дублей и устаревшего кэша в клиенте Discord
            await restClient.BulkOverwriteGlobalApplicationCommandsAsync(_applicationId, []);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось очистить глобальные команды приложения {AppId}", _applicationId);
        }

        foreach (var guildId in arg.GuildIds)
            await SyncGuildAsync(_applicationId, guildId);
    }

    public async ValueTask HandleAsync(GuildCreateEventArgs arg)
    {
        var appId = _applicationId;
        if (appId == 0)
        {
            try
            {
                var me = await restClient.GetCurrentUserAsync();
                appId = _applicationId = me.Id;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось определить ApplicationId при GuildCreate для {GuildId}", arg.GuildId);
                return;
            }
        }

        await SyncGuildAsync(appId, arg.GuildId);
    }

    private async ValueTask SyncGuildAsync(ulong appId, ulong guildId)
    {
        if (!_syncedGuilds.TryAdd(guildId, 0))
            return;

        try
        {
            var registered = await commandServiceManager.RegisterCommandsAsync(restClient, appId, guildId);
            logger.LogInformation(
                "Мгновенно зарегистрировано {Count} групп slash-команд для сервера {GuildId}",
                registered.Count,
                guildId);
        }
        catch (Exception ex)
        {
            _syncedGuilds.TryRemove(guildId, out _);
            logger.LogError(ex, "Ошибка регистрации slash-команд для сервера {GuildId}", guildId);
        }
    }
}
