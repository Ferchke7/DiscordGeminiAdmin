using DiscordGeminiAdmin.Domain.Moderation.Repositories;

namespace DiscordGeminiAdmin.Infrastructure.BackgroundJobs;

public sealed class RetentionCleanupJob(
    IBufferedMessageRepository messages,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<RetentionCleanupJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var retentionHours = configuration.GetValue("Retention:MessageBufferHours", 24);
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddHours(-retentionHours);
        var deleted = await messages.DeleteOlderThanAsync(cutoff, ct);
        if (deleted > 0)
            logger.LogInformation("Очищено {Count} сообщений старше {Hours} ч из буфера", deleted, retentionHours);
    }
}
