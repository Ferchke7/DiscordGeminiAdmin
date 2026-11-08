using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using DiscordGeminiAdmin.Domain.Digests.Repositories;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using DiscordGeminiAdmin.Domain.Knowledge.Repositories;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Moderation.Repositories;
using DiscordGeminiAdmin.Domain.Structure.Repositories;
using DiscordGeminiAdmin.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace DiscordGeminiAdmin.Infrastructure.Persistence;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPersistence(IConfiguration configuration)
        {
            services.AddNpgsql<DatabaseContext>(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.UseVector(),
                dbContext => dbContext.UseSnakeCaseNamingConvention());

            services.AddSingleton(BindDefaultModerationPolicy(configuration));
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IGuildProfileRepository, GuildProfileRepository>();
            services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
            services.AddScoped<IModerationCaseRepository, ModerationCaseRepository>();
            services.AddScoped<IBufferedMessageRepository, BufferedMessageRepository>();
            services.AddScoped<IHourlyDigestRepository, HourlyDigestRepository>();
            services.AddScoped<IStructureProposalRepository, StructureProposalRepository>();
            return services;
        }
    }

    private static ModerationPolicy BindDefaultModerationPolicy(IConfiguration configuration)
    {
        var section = configuration.GetSection("AutoBan");
        return new ModerationPolicy(
            MinConfidence: section.GetValue("MinConfidence", 0.95),
            MinEvidenceMessages: section.GetValue("MinEvidenceMessages", 3),
            MaxAutoBansPerHour: section.GetValue("MaxPerHour", 3),
            DryRun: section.GetValue("DryRun", true));
    }
}
