using System;
using System.Linq;
using System.Security.Claims;

namespace Cwi.Orchestrator.Api.Auth;

public static class AdminEntitlementEvaluator
{
    public static bool HasAdminEntitlement(ClaimsPrincipal user)
    {
        if (string.Equals(user.FindFirst("is_superadmin")?.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (user.Claims.Any(claim =>
            string.Equals(claim.Type, "cwi_admin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return user.Claims.Any(IsPlatformAdminClaim);
    }

    private static bool IsPlatformAdminClaim(Claim claim)
    {
        if (claim.Type is "role" or "roles" || claim.Type == ClaimTypes.Role)
        {
            return claim.Value
                .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(value => string.Equals(value, "platform_admin", StringComparison.OrdinalIgnoreCase));
        }

        if (claim.Type is "scope" or "scp")
        {
            return claim.Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(value => string.Equals(value, "platform_admin", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}
