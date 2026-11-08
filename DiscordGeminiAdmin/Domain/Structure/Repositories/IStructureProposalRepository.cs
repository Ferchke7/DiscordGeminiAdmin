namespace DiscordGeminiAdmin.Domain.Structure.Repositories;

public interface IStructureProposalRepository
{
    void Add(StructureProposal proposal);
    Task<StructureProposal?> GetByIdAsync(Guid id, CancellationToken ct);
}
