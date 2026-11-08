using Hangfire;

namespace DiscordGeminiAdmin.Infrastructure.BackgroundJobs;

internal sealed class RecurringJobsHostedService(
    IRecurringJobManager recurringJobs,
    IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var digestCron = configuration.GetValue("Digest:Cron", "0 * * * *") ?? "0 * * * *";

        recurringJobs.AddOrUpdate<HourlyModerationJob>(
            "hourly-moderation",
            job => job.ExecuteAsync(CancellationToken.None),
            digestCron);

        recurringJobs.AddOrUpdate<RetentionCleanupJob>(
            "retention-cleanup",
            job => job.ExecuteAsync(CancellationToken.None),
            "15 * * * *");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
