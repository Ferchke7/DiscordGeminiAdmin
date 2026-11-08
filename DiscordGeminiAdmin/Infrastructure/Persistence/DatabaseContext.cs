using DiscordGeminiAdmin.Domain.Digests;
using DiscordGeminiAdmin.Domain.Guilds;
using DiscordGeminiAdmin.Domain.Knowledge;
using DiscordGeminiAdmin.Domain.Moderation;
using DiscordGeminiAdmin.Domain.Structure;
using DiscordGeminiAdmin.Infrastructure.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace DiscordGeminiAdmin.Infrastructure.Persistence;

public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
    public DbSet<GuildProfile> GuildProfiles => Set<GuildProfile>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunkRecord> KnowledgeChunks => Set<KnowledgeChunkRecord>();
    public DbSet<BufferedMessage> BufferedMessages => Set<BufferedMessage>();
    public DbSet<ModerationCase> ModerationCases => Set<ModerationCase>();
    public DbSet<HourlyDigest> HourlyDigests => Set<HourlyDigest>();
    public DbSet<StructureProposal> StructureProposals => Set<StructureProposal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DatabaseContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
