using System.Collections.Concurrent;
using System.Collections.Immutable;
using Lavalink4NET.Clients;
using Lavalink4NET.Clients.Events;
using Lavalink4NET.Events;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using LavalinkVoiceState = Lavalink4NET.Clients.VoiceState;

namespace DiscordGeminiAdmin.Infrastructure.Audio;

/// <summary>
/// Адаптер между NetCord GatewayClient и Lavalink4NET IDiscordClientWrapper.
/// Передаёт события смены голосовых серверов и состояний участников в Lavalink,
/// и транслирует запросы на подключение бота к голосовым каналам через GatewayClient.
/// </summary>
public sealed class NetCordLavalinkClientWrapper : IDiscordClientWrapper, IDisposable
{
    private readonly GatewayClient _gatewayClient;
    private readonly ILogger<NetCordLavalinkClientWrapper> _logger;
    private readonly ConcurrentDictionary<ulong, LavalinkVoiceState> _voiceStates = new();
    private readonly TaskCompletionSource<ClientInformation> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ulong _currentUserId;

    public event AsyncEventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;
    public event AsyncEventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    public NetCordLavalinkClientWrapper(
        GatewayClient gatewayClient,
        ILogger<NetCordLavalinkClientWrapper> logger)
    {
        _gatewayClient = gatewayClient;
        _logger = logger;

        _gatewayClient.Ready += HandleReadyAsync;
        _gatewayClient.VoiceServerUpdate += HandleVoiceServerUpdateAsync;
        _gatewayClient.VoiceStateUpdate += HandleVoiceStateUpdateAsync;
    }

    public async ValueTask<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        var currentId = _currentUserId != 0 ? _currentUserId : _gatewayClient.Cache.User?.Id ?? _gatewayClient.Id;
        if (currentId != 0)
        {
            return new ClientInformation("NetCord", currentId, 1);
        }

        using var registration = cancellationToken.Register(() => _readyTcs.TrySetCanceled(cancellationToken));
        return await _readyTcs.Task.ConfigureAwait(false);
    }

    public async ValueTask SendVoiceUpdateAsync(
        ulong guildId,
        ulong? voiceChannelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var props = new VoiceStateProperties(guildId, voiceChannelId)
            {
                SelfDeaf = selfDeaf,
                SelfMute = selfMute
            };

            await _gatewayClient.UpdateVoiceStateAsync(props, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обновления голосового состояния бота в гильдии {GuildId}", guildId);
            throw;
        }
    }

    public ValueTask<ImmutableArray<ulong>> GetChannelUsersAsync(
        ulong guildId,
        ulong voiceChannelId,
        bool includeBots = false,
        CancellationToken cancellationToken = default)
    {
        if (_gatewayClient.Cache.Guilds.TryGetValue(guildId, out var guild))
        {
            var users = guild.VoiceStates.Values
                .Where(s => s.ChannelId == voiceChannelId)
                .Where(s => includeBots || (guild.Users.TryGetValue(s.UserId, out var u) && !u.IsBot))
                .Select(s => s.UserId)
                .ToImmutableArray();

            return ValueTask.FromResult(users);
        }

        return ValueTask.FromResult(ImmutableArray<ulong>.Empty);
    }

    private ValueTask HandleReadyAsync(ReadyEventArgs args)
    {
        _currentUserId = args.User.Id;
        _readyTcs.TrySetResult(new ClientInformation("NetCord", args.User.Id, 1));
        return ValueTask.CompletedTask;
    }

    private async ValueTask HandleVoiceServerUpdateAsync(VoiceServerUpdateEventArgs args)
    {
        if (VoiceServerUpdated is { } handler && !string.IsNullOrWhiteSpace(args.Endpoint))
        {
            var voiceServer = new VoiceServer(args.Token, args.Endpoint);
            var eventArgs = new VoiceServerUpdatedEventArgs(args.GuildId, voiceServer);
            await handler.Invoke(this, eventArgs).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleVoiceStateUpdateAsync(NetCord.Gateway.VoiceState args)
    {
        var currentUserId = _currentUserId != 0 ? _currentUserId : _gatewayClient.Cache.User?.Id ?? _gatewayClient.Id;
        var isCurrentUser = currentUserId != 0 && args.UserId == currentUserId;

        var newVoiceState = new LavalinkVoiceState(args.ChannelId, args.SessionId);
        _voiceStates.TryGetValue(args.UserId, out var oldVoiceState);
        _voiceStates[args.UserId] = newVoiceState;

        if (VoiceStateUpdated is { } handler)
        {
            var eventArgs = new VoiceStateUpdatedEventArgs(
                args.GuildId,
                args.UserId,
                isCurrentUser,
                newVoiceState,
                oldVoiceState);

            await handler.Invoke(this, eventArgs).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _gatewayClient.Ready -= HandleReadyAsync;
        _gatewayClient.VoiceServerUpdate -= HandleVoiceServerUpdateAsync;
        _gatewayClient.VoiceStateUpdate -= HandleVoiceStateUpdateAsync;
    }
}
