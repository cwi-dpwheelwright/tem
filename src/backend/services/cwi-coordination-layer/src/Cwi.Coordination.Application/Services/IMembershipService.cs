using Cwi.Common;
using Cwi.Coordination.Application.Models;
namespace Cwi.Coordination.Application.Services;

public interface IMembershipService
{
    Task<ServiceResponse<EnrollmentResponse>> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken);
    Task<ServiceResponse<MemberResponse>> RotateAsync(RotationRequest request, CancellationToken cancellationToken);
    Task<ServiceResponse<MemberResponse>> RevokeAsync(RevokeRequest request, CancellationToken cancellationToken);
    Task<ServiceResponse<IReadOnlyList<MemberResponse>>> ListAsync(CancellationToken cancellationToken);
}
