using DiscordGeminiAdmin.Application.Dependencies.Knowledge;

namespace DiscordGeminiAdmin.Infrastructure.Knowledge;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddKnowledge()
        {
            services.AddSingleton<ITextChunker, MarkdownTextChunker>();
            services.AddScoped<IKnowledgeIndex, PgVectorKnowledgeIndex>();
            return services;
        }
    }
}
