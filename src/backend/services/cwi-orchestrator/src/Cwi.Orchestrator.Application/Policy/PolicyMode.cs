#nullable enable
namespace Cwi.Orchestrator.Application.Policy;

public enum PolicyMode
{
    Observe,
    Recommend,
    Approve,
    AutoWithinBounds
}

public static class PolicyModeParser
{
    public static bool TryParse(string value, out PolicyMode mode)
    {
        if (string.Equals(value, "standard", StringComparison.OrdinalIgnoreCase))
        {
            mode = PolicyMode.Observe;
            return true;
        }

        if (string.Equals(value, "auto_within_bounds", StringComparison.OrdinalIgnoreCase))
        {
            mode = PolicyMode.AutoWithinBounds;
            return true;
        }

        if (Enum.TryParse(value, ignoreCase: true, out mode))
        {
            return true;
        }

        mode = PolicyMode.Observe;
        return false;
    }
}
#nullable disable
