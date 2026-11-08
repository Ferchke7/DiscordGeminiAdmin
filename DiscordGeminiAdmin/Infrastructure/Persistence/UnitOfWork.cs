using DiscordGeminiAdmin.Application.Dependencies.Persistence;
using Wolverine.EntityFrameworkCore;

namespace DiscordGeminiAdmin.Infrastructure.Persistence;

internal sealed class UnitOfWork(IDbContextOutbox<DatabaseContext> outbox) : IUnitOfWork
{
    public Task SaveAsync(CancellationToken ct = default) =>
        outbox.SaveChangesAndFlushMessagesAsync(ct);
}
