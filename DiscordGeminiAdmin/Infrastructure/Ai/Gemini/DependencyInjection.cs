using DiscordGeminiAdmin.Application.Dependencies.Ai;
using DiscordGeminiAdmin.Application.Dependencies.Knowledge;
using Google.GenAI;
using Microsoft.Extensions.Options;

namespace DiscordGeminiAdmin.Infrastructure.Ai.Gemini;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddGeminiAi(IConfiguration configuration)
        {
            services.Configure<GeminiSettings>(configuration.GetSection(GeminiSettings.SectionName));
            services.AddSingleton(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<GeminiSettings>>().Value;
                return string.IsNullOrWhiteSpace(settings.ApiKey)
                    ? new Client()
                    : new Client(apiKey: settings.ApiKey);
            });
            services.AddScoped<IEmbeddingService, GeminiEmbeddingService>();
            services.AddScoped<IModerationAi, GeminiModerationAi>();
            services.AddScoped<IStructureAdvisorAi, GeminiStructureAdvisorAi>();
            return services;
        }
    }
}
