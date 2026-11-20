namespace Cwi.Coordination.DataAccess;

public sealed class BackhaulPolicyRecord
{
    public Guid Id { get; set; }
    public bool RequireAuthenticatedChannel { get; set; }
    public string AllowedInterfacesJson { get; set; } = "[]";
    public string DeniedInterfacesJson { get; set; } = "[]";
    public string RevokedPeersJson { get; set; } = "[]";
    public string PolicyVersion { get; set; } = "1.0";
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
