using DiscordGeminiAdmin.Application.UseCases.Moderation.RunHourly;
using DiscordGeminiAdmin.Domain.Guilds.Repositories;
using FluentResults;
using Wolverine;

namespace DiscordGeminiAdmin.Infrastructure.BackgroundJobs;

public sealed class HourlyModerationJob(
    IGuildProfileRepository guilds,
    IMessageBus bus,
    ILogger<HourlyModerationJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var profiles = await guilds.GetAllAsync(ct);
        foreach (var profile in profiles)
        {
            var result = await bus.InvokeAsync<Result<RunHourlyModerationSummary>>(
                new RunHourlyModeration(profile.GuildId),
                ct);

            if (result.IsFailed)
            {
                logger.LogWarning(
                    "Часовой прогон модерации гильдии {GuildId} завершился с ошибкой: {Errors}",
                    profile.GuildId.Value,
                    string.Join("; ", result.Errors.Select(e => e.Message)));
            }
        }
    }
}
