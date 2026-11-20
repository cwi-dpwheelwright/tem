using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Packs;

public sealed class LegacyPackResolver : IPackContractResolver
{
    private readonly PackFetcher _packFetcher;
    private readonly IPackSignatureValidator _signatureValidator;

    public LegacyPackResolver(PackFetcher packFetcher, IPackSignatureValidator signatureValidator)
    {
        _packFetcher = packFetcher;
        _signatureValidator = signatureValidator;
    }

    public PackContractType ContractType => PackContractType.Legacy;

    public async Task<ServiceResponse<PackDocument>> ResolveAsync(string packName, CancellationToken cancellationToken)
    {
        var fetch = await _packFetcher.FetchAsync(packName, cancellationToken).ConfigureAwait(false);
        if (!fetch.IsSuccess || fetch.Data is null)
        {
            return fetch;
        }

        var signature = _signatureValidator.Validate(fetch.Data);
        if (!signature.IsSuccess)
        {
            return ServiceResponse<PackDocument>.Fail(
                signature.Error ?? new DomainError(ErrorCodes.InvalidRequest, "Pack signature invalid."),
                statusCode: signature.StatusCode,
                message: signature.Message);
        }

        return fetch;
    }
}
