using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;

namespace DiscordGeminiAdmin.Presentation.Discord;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDiscord()
        {
            services.AddDiscordGateway(options =>
            {
                // MessageContent / GuildUsers - привилегированные интенты: включить в Developer Portal.
                options.Intents = GatewayIntents.Guilds
                                  | GatewayIntents.GuildMessages
                                  | GatewayIntents.MessageContent
                                  | GatewayIntents.GuildUsers
                                  | GatewayIntents.GuildModeration;
            });
            services.AddApplicationCommands(options =>
            {
                // Регистрируем команды мгновенно на уровне гильдий через GuildCommandSyncGatewayHandler
                options.AutoRegisterCommands = false;
            });
            services.AddComponentInteractions<NetCord.ButtonInteraction, ButtonInteractionContext>();
            services.AddGatewayHandlers(typeof(DependencyInjection).Assembly);
            return services;
        }
    }

    extension(IHost host)
    {
        public IHost UseDiscord()
        {
            host.AddModules(typeof(DependencyInjection).Assembly);
            return host;
        }
    }
}
