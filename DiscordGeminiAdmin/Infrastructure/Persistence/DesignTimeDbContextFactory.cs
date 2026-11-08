using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace DiscordGeminiAdmin.Infrastructure.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
    public DatabaseContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DatabaseContext>();
        optionsBuilder
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=discord_gemini_admin;Username=dga;Password=dga",
                npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention();

        return new DatabaseContext(optionsBuilder.Options);
    }
}
