namespace DiscordGeminiAdmin.Domain.Digests.Repositories;

public interface IHourlyDigestRepository
{
    void Add(HourlyDigest digest);
    Task<HourlyDigest?> GetByIdAsync(Guid id, CancellationToken ct);
}
