using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface IMembershipRepository
{
    Task<Member> AddAsync(Member member, CancellationToken cancellationToken);
    Task<Member?> GetAsync(string nodeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Member>> ListAsync(CancellationToken cancellationToken);
    Task<Member> UpdateAsync(Member member, CancellationToken cancellationToken);
}
