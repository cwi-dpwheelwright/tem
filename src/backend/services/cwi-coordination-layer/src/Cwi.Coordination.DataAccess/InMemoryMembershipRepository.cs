using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryMembershipRepository : IMembershipRepository
{
    private readonly Dictionary<string, Member> _members = new(StringComparer.OrdinalIgnoreCase);

    public Task<Member> AddAsync(Member member, CancellationToken cancellationToken)
    {
        _members[member.NodeId] = member;
        return Task.FromResult(member);
    }

    public Task<Member?> GetAsync(string nodeId, CancellationToken cancellationToken)
    {
        _members.TryGetValue(nodeId, out var member);
        return Task.FromResult(member);
    }

    public Task<IReadOnlyList<Member>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult((IReadOnlyList<Member>)_members.Values.OrderBy(x => x.NodeId).ToList());
    }

    public Task<Member> UpdateAsync(Member member, CancellationToken cancellationToken)
    {
        _members[member.NodeId] = member;
        return Task.FromResult(member);
    }
}
