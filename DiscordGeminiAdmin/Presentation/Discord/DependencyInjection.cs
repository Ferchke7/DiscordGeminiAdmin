using DiscordGeminiAdmin.Presentation.Discord.Events;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ApplicationCommands;
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
                                  | GatewayIntents.GuildModeration
                                  | GatewayIntents.GuildVoiceStates;
            });
            var errorHandler = new DiscordErrorResultHandler();
            services.AddApplicationCommands(options =>
            {
                // Регистрируем команды мгновенно на уровне гильдий через GuildCommandSyncGatewayHandler
                options.AutoRegisterCommands = false;
                options.ResultHandler = errorHandler;
            });
            services.AddComponentInteractions<NetCord.ButtonInteraction, ButtonInteractionContext>(options =>
            {
                options.ResultHandler = errorHandler;
            });
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
