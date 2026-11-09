using Lavalink4NET;
using Lavalink4NET.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordGeminiAdmin.Infrastructure.Audio;

internal static class DependencyInjection
{
    public static IServiceCollection AddAudio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLavalink<NetCordLavalinkClientWrapper>();
        services.ConfigureLavalink(options =>
        {
            var baseAddress = configuration["Lavalink:BaseAddress"] ?? "http://dga-lavalink:2333";
            var wsAddress = configuration["Lavalink:WebSocketAddress"] ?? "ws://dga-lavalink:2333/v4/websocket";
            options.BaseAddress = new Uri(baseAddress);
            options.WebSocketUri = new Uri(wsAddress);
            options.Passphrase = configuration["Lavalink:Passphrase"] ?? "youshallnotpass";
        });

        return services;
    }
}
