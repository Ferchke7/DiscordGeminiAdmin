using Hangfire;
using Hangfire.PostgreSql;

namespace DiscordGeminiAdmin.Infrastructure.BackgroundJobs;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddBackgroundJobs(IConfiguration configuration)
        {
            services.AddHangfire(hangfire =>
            {
                hangfire.UseSimpleAssemblyNameTypeSerializer();
                hangfire.UseRecommendedSerializerSettings();
                hangfire.UsePostgreSqlStorage(options =>
                {
                    options.UseNpgsqlConnection(configuration.GetConnectionString("DefaultConnection"));
                });
            });
            services.AddHangfireServer();
            services.AddScoped<HourlyModerationJob>();
            services.AddScoped<RetentionCleanupJob>();
            services.AddHostedService<RecurringJobsHostedService>();
            return services;
        }
    }
}
