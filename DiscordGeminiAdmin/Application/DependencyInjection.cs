using Microsoft.Extensions.DependencyInjection;

namespace DiscordGeminiAdmin.Application;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApplication()
        {
            services.AddSingleton(TimeProvider.System);
            return services;
        }
    }
}
