namespace Cwi.Orchestrator.Domain.Entities;

public sealed class HomeNodePolicyScope
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string PolicyScopeId { get; set; } = string.Empty;
    public string RulesJson { get; set; } = "{}";
    public string Version { get; set; } = "1.0";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
