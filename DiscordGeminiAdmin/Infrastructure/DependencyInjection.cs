using DiscordGeminiAdmin.Infrastructure.Ai.Gemini;
using DiscordGeminiAdmin.Infrastructure.BackgroundJobs;
using DiscordGeminiAdmin.Infrastructure.Discord;
using DiscordGeminiAdmin.Infrastructure.Knowledge;
using DiscordGeminiAdmin.Infrastructure.Messaging;
using DiscordGeminiAdmin.Infrastructure.Persistence;

namespace DiscordGeminiAdmin.Infrastructure;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInfrastructure(IConfiguration configuration)
        {
            return services
                .AddPersistence(configuration)
                .AddGeminiAi(configuration)
                .AddKnowledge()
                .AddDiscordAdapters()
                .AddMessaging(configuration)
                .AddBackgroundJobs(configuration);
        }
    }
}
