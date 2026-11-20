using Cwi.Common;

namespace Cwi.Orchestrator.Application.Packs;

public interface IPackContractResolver
{
    PackContractType ContractType { get; }
    Task<ServiceResponse<PackDocument>> ResolveAsync(string packName, CancellationToken cancellationToken);
}
