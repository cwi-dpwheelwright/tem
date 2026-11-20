using Cwi.Common;

namespace Cwi.Orchestrator.Application.Packs;

public interface IPackSignatureValidator
{
    ServiceResponse<bool> Validate(PackDocument document);
}
