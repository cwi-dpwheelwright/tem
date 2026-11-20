namespace Cwi.PackService.Api.Configuration;

public sealed class AuditConfig
{
    public string? AuditUrl { get; init; }
    public int TimeoutSeconds { get; init; }

    public static AuditConfig FromEnvironment()
    {
        var auditUrl = Environment.GetEnvironmentVariable("PACK_AUDIT_URL");
        var timeoutSeconds = GetIntFromEnv("PACK_AUDIT_TIMEOUT_SECONDS", 2);
        return new AuditConfig { AuditUrl = auditUrl, TimeoutSeconds = timeoutSeconds };
    }

    private static int GetIntFromEnv(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var value) ? value : fallback;
    }
}
