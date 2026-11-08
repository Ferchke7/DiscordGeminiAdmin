using DiscordGeminiAdmin.Application.Dependencies.Discord;

namespace DiscordGeminiAdmin.Infrastructure.Discord;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDiscordAdapters()
        {
            services.AddScoped<IGuildGateway, NetCordGuildGateway>();
            services.AddScoped<IDiscordNotifier, NetCordDiscordNotifier>();
            return services;
        }
    }
}
