namespace DiscordGeminiAdmin.Application.Dependencies.Persistence;

public interface IUnitOfWork
{
    Task SaveAsync(CancellationToken ct = default);
}
