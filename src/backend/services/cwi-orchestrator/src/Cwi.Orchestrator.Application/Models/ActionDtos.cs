#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class ActionRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("policy_mode")]
    public string PolicyMode { get; init; } = string.Empty;

    [JsonPropertyName("action")]
    public ActionDescriptorDto Action { get; init; } = new();

    [JsonPropertyName("pack_hash")]
    public string PackHash { get; init; } = string.Empty;

    [JsonPropertyName("pack_version")]
    public string PackVersion { get; init; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string ModelId { get; init; } = string.Empty;

    [JsonPropertyName("policy_version")]
    public string PolicyVersion { get; init; } = string.Empty;
}

public sealed class ActionDescriptorDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public Dictionary<string, object?> Payload { get; init; } = new();
}

public sealed class ApprovalTicketDto
{
    [JsonPropertyName("ticket_id")]
    public string TicketId { get; init; } = string.Empty;

    [JsonPropertyName("risk")]
    public string Risk { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class ActionResponseDto
{
    [JsonPropertyName("executed")]
    public bool Executed { get; init; }

    [JsonPropertyName("approval_ticket")]
    public ApprovalTicketDto? ApprovalTicket { get; init; }

    [JsonPropertyName("result")]
    public Dictionary<string, object?>? Result { get; init; }

    [JsonPropertyName("trace")]
    public TracePayload Trace { get; init; } = new();
}

public sealed class ApprovalCompleteRequestDto
{
    [JsonPropertyName("ticket_id")]
    public string TicketId { get; init; } = string.Empty;

    [JsonPropertyName("decision")]
    public string Decision { get; init; } = string.Empty;

    [JsonPropertyName("approver_id")]
    public string ApproverId { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class ApprovalCompleteResponseDto
{
    [JsonPropertyName("ticket_id")]
    public string TicketId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("executed")]
    public bool Executed { get; init; }

    [JsonPropertyName("trace")]
    public TracePayload Trace { get; init; } = new();
}

public sealed class ApprovalTicketStatusDto
{
    [JsonPropertyName("ticket_id")]
    public string TicketId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("risk")]
    public string Risk { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}
#nullable disable
