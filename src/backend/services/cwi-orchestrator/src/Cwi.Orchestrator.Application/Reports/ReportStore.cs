#nullable enable
using System.Collections.Concurrent;
using System.Linq;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Reports;

public sealed class ReportStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<ReportRecord>> _reportsByTenant =
        new(StringComparer.OrdinalIgnoreCase);

    public ReportStore()
    {
        if (IsDemoDataEnabled())
        {
            Seed();
        }
    }

    private static bool IsDemoDataEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("CWI_ENABLE_DEMO_DATA");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
    }

    public IReadOnlyList<ReportSummaryDto> List(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Array.Empty<ReportSummaryDto>();
        }

        if (!_reportsByTenant.TryGetValue(tenantId.Trim(), out var records))
        {
            return Array.Empty<ReportSummaryDto>();
        }

        return records
            .OrderByDescending(report => report.CreatedUtc)
            .Select(ToSummary)
            .ToList();
    }

    public ReportDetailDto? Get(string tenantId, string reportId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(reportId))
        {
            return null;
        }

        if (!_reportsByTenant.TryGetValue(tenantId.Trim(), out var records))
        {
            return null;
        }

        var record = records.FirstOrDefault(report =>
            string.Equals(report.ReportId, reportId, StringComparison.OrdinalIgnoreCase));
        return record is null ? null : ToDetail(record);
    }

    private void Seed()
    {
        var tenantId = "tenant-demo";
        _reportsByTenant[tenantId] = new List<ReportRecord>
        {
            new(
                ReportId: "rep-2026-02-01-ops-001",
                Title: "Autonomous safety sweep - North perimeter",
                CreatedUtc: DateTimeOffset.Parse("2026-02-01T12:40:00Z"),
                Status: "approved",
                CoherenceScore: 0.94,
                ConflictCount: 1,
                Summary: "Multi-sensor sweep completed with minor variance in thermal readings; no anomalies detected.",
                Narrative:
                    "The North perimeter sweep executed under standard governance. The drone mesh observed minor divergence in thermal bands during the final pass, resolved via consensus alignment. No compliance exceptions were triggered, and the operator confirmed safe clearance for resuming normal operations.",
                Structured: new Dictionary<string, string>
                {
                    ["Primary objective"] = "Safety sweep + anomaly detection",
                    ["Active nodes"] = "14 mesh nodes",
                    ["Risk posture"] = "Low",
                    ["Approval gating"] = "Auto-approved",
                    ["Discrepancy severity"] = "Low"
                },
                Evidence: new List<ReportEvidenceDto>
                {
                    new() { Label = "Trace ID", Value = "trace-8f3a" },
                    new() { Label = "Sensor delta", Value = "0.12% thermal variance" },
                    new() { Label = "Operator sign-off", Value = "O-217" }
                },
                EscalationTicket: null
            ),
            new(
                ReportId: "rep-2026-02-01-ops-002",
                Title: "Compliance procedure update - Zone 3",
                CreatedUtc: DateTimeOffset.Parse("2026-02-01T14:05:00Z"),
                Status: "escalated",
                CoherenceScore: 0.71,
                ConflictCount: 4,
                Summary: "Procedure text and runtime actions diverged; approval required before deploying update.",
                Narrative:
                    "The ingestion pipeline detected inconsistencies between the policy pack and the observed operator workflow in Zone 3. Conflict vectors highlight divergent action sequences and missing approvals. Escalation is required to align the governance policy before deployment.",
                Structured: new Dictionary<string, string>
                {
                    ["Primary objective"] = "Procedure update validation",
                    ["Active nodes"] = "9 mesh nodes",
                    ["Risk posture"] = "Medium",
                    ["Approval gating"] = "Escalated",
                    ["Discrepancy severity"] = "High"
                },
                Evidence: new List<ReportEvidenceDto>
                {
                    new() { Label = "Conflict vectors", Value = "4 mismatched steps" },
                    new() { Label = "Policy pack", Value = "zone-3-proc@1.8.2" },
                    new() { Label = "Ticket", Value = "APR-2198" }
                },
                EscalationTicket: "APR-2198"
            ),
            new(
                ReportId: "rep-2026-02-02-ops-003",
                Title: "Energy optimization run - East subgrid",
                CreatedUtc: DateTimeOffset.Parse("2026-02-02T08:20:00Z"),
                Status: "review",
                CoherenceScore: 0.82,
                ConflictCount: 2,
                Summary: "Energy balancing executed with two low-risk conflicts flagged for follow-up.",
                Narrative:
                    "Balancing actions reduced peak demand by 4.2%. Two signals showed missing operator confirmation due to telemetry gaps. The report recommends an audit of the telemetry sampling policy before the next optimization.",
                Structured: new Dictionary<string, string>
                {
                    ["Primary objective"] = "Energy optimization",
                    ["Active nodes"] = "21 mesh nodes",
                    ["Risk posture"] = "Medium",
                    ["Approval gating"] = "Manual review",
                    ["Discrepancy severity"] = "Moderate"
                },
                Evidence: new List<ReportEvidenceDto>
                {
                    new() { Label = "Savings", Value = "4.2% demand reduction" },
                    new() { Label = "Telemetry gaps", Value = "2 missing confirmations" },
                    new() { Label = "Operator", Value = "O-093" }
                },
                EscalationTicket: null
            ),
            new(
                ReportId: "rep-2026-02-02-ops-004",
                Title: "Incident response drill - South corridor",
                CreatedUtc: DateTimeOffset.Parse("2026-02-02T11:15:00Z"),
                Status: "draft",
                CoherenceScore: 0.97,
                ConflictCount: 0,
                Summary: "Drill executed successfully; awaiting operator narrative finalization.",
                Narrative:
                    "The response drill followed the planned route with no deviations. Awaiting narrative sign-off and final operator notes before publishing.",
                Structured: new Dictionary<string, string>
                {
                    ["Primary objective"] = "Incident response drill",
                    ["Active nodes"] = "12 mesh nodes",
                    ["Risk posture"] = "Low",
                    ["Approval gating"] = "Draft",
                    ["Discrepancy severity"] = "None"
                },
                Evidence: new List<ReportEvidenceDto>
                {
                    new() { Label = "Drill ID", Value = "DR-108" },
                    new() { Label = "Latency", Value = "< 120 ms" },
                    new() { Label = "Operator", Value = "O-054" }
                },
                EscalationTicket: null
            )
        };
    }

    private static ReportSummaryDto ToSummary(ReportRecord record)
    {
        return new ReportSummaryDto
        {
            ReportId = record.ReportId,
            Title = record.Title,
            CreatedUtc = record.CreatedUtc,
            Status = record.Status,
            CoherenceScore = record.CoherenceScore,
            ConflictCount = record.ConflictCount,
            Summary = record.Summary,
            EscalationTicket = record.EscalationTicket
        };
    }

    private static ReportDetailDto ToDetail(ReportRecord record)
    {
        return new ReportDetailDto
        {
            ReportId = record.ReportId,
            Title = record.Title,
            CreatedUtc = record.CreatedUtc,
            Status = record.Status,
            CoherenceScore = record.CoherenceScore,
            ConflictCount = record.ConflictCount,
            Summary = record.Summary,
            EscalationTicket = record.EscalationTicket,
            Narrative = record.Narrative,
            Structured = record.Structured,
            Evidence = record.Evidence
        };
    }

    private sealed record ReportRecord(
        string ReportId,
        string Title,
        DateTimeOffset CreatedUtc,
        string Status,
        double CoherenceScore,
        int ConflictCount,
        string Summary,
        string Narrative,
        IReadOnlyDictionary<string, string> Structured,
        IReadOnlyList<ReportEvidenceDto> Evidence,
        string? EscalationTicket);
}
#nullable disable
