using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Graph;

public sealed class ConnectorGraphProjectionBuilderTests
{
    [Fact]
    public void Build_projects_normalized_connector_record_into_memory_evidence_graph()
    {
        var builder = new ConnectorGraphProjectionBuilder();
        var request = new IngestDocumentRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            Tags = new List<string> { "connector_normalized", "connector_normalized_v1", "connector:email" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = "record-1",
                Title = "Client deadline",
                Content = """
                    schema_version: cwi.connector.normalized.v1
                    record_id: record-1
                    connector_type: email
                    body:
                    Jordan Smith emailed: Please follow up by Friday on the client deadline.
                    normalized_record_json:
                    {
                      "entities": [
                        { "type": "person", "value": "Jordan Smith", "confidence": 0.92 }
                      ]
                    }
                    """,
                ContentEncoding = "plain",
                MimeType = "application/json",
                Source = "connector_normalized:email",
                CreatedUtc = DateTimeOffset.Parse("2026-04-29T15:00:00Z"),
                Metadata = new Dictionary<string, object?>
                {
                    ["normalization_mode"] = "connector_normalized_v1",
                    ["record_id"] = "record-1",
                    ["connector_type"] = "email",
                    ["source_system"] = "outlook",
                    ["account_id"] = "acct-1",
                    ["source_record_id"] = "msg-1",
                    ["life_work_scope"] = "work",
                    ["sensitivity_label"] = "private_work",
                    ["candidate_memory_domains"] = "work_career",
                    ["candidate_memory_types"] = "task_memory",
                    ["confidence"] = 0.85,
                    ["source_sha256"] = "sha-1"
                }
            }
        };

        var projection = builder.Build(request, "trace-1");

        projection.Nodes.Should().Contain(node => node.Labels.Contains("SourceRecord"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Memory"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("MemoryVersion"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Domain"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Entity"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Task"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Deadline"));
        projection.Edges.Should().Contain(edge => edge.Relation == "PRODUCED");
        projection.Edges.Should().Contain(edge => edge.Relation == "CURRENT_VERSION");
        projection.Edges.Should().Contain(edge => edge.Relation == "EVIDENCED_BY");
        projection.Edges.Should().Contain(edge => edge.Relation == "BELONGS_TO_DOMAIN");
        projection.Edges.Should().Contain(edge => edge.Relation == "MENTIONS");
        projection.Edges.Should().Contain(edge => edge.Relation == "HAS_TASK");
        projection.Edges.Should().Contain(edge => edge.Relation == "DUE_ON");
    }

    [Fact]
    public void Build_projects_connector_tasks_into_goal_risk_and_cross_domain_edges()
    {
        var builder = new ConnectorGraphProjectionBuilder();
        var request = BuildConnectorRequest(
            "record-2",
            """
            schema_version: cwi.connector.normalized.v1
            record_id: record-2
            connector_type: work_apps
            body:
            Ticket CWI-102 is blocked by a missing API token and must be fixed by Friday for the launch goal.
            normalized_record_json:
            {
              "entities": [
                { "type": "person", "value": "Demitrius", "confidence": 0.92 }
              ]
            }
            """,
            new Dictionary<string, object?>
            {
                ["normalization_mode"] = "connector_normalized_v1",
                ["record_id"] = "record-2",
                ["connector_type"] = "work_apps",
                ["source_system"] = "linear",
                ["account_id"] = "acct-1",
                ["source_record_id"] = "ticket-1",
                ["life_work_scope"] = "mixed",
                ["sensitivity_label"] = "private_work",
                ["candidate_memory_domains"] = "work_career,technology",
                ["candidate_memory_types"] = "task_memory,issue_memory,goal_memory",
                ["confidence"] = 0.9,
                ["source_sha256"] = "sha-2"
            });

        var projection = builder.Build(request, "trace-2");

        projection.Nodes.Should().Contain(node => node.Labels.Contains("Task"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Goal"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Issue"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Risk"));
        projection.Edges.Should().Contain(edge => edge.Relation == "HAS_TASK");
        projection.Edges.Should().Contain(edge => edge.Relation == "SUPPORTS_GOAL");
        projection.Edges.Should().Contain(edge => edge.Relation == "DUE_ON");
        projection.Edges.Should().Contain(edge => edge.Relation == "BLOCKED_BY");
        projection.Edges.Should().Contain(edge => edge.Relation == "HAS_RISK");
        projection.Edges.Should().Contain(edge => edge.Relation == "CROSSES_DOMAIN");
    }

    [Fact]
    public void Build_projects_connector_decisions_to_projects()
    {
        var builder = new ConnectorGraphProjectionBuilder();
        var request = BuildConnectorRequest(
            "record-3",
            """
            schema_version: cwi.connector.normalized.v1
            record_id: record-3
            connector_type: documents
            body:
            Decision approved Project Atlas renewal approach for the May review.
            normalized_record_json:
            {
              "entities": []
            }
            """,
            new Dictionary<string, object?>
            {
                ["normalization_mode"] = "connector_normalized_v1",
                ["record_id"] = "record-3",
                ["connector_type"] = "documents",
                ["source_system"] = "sharepoint",
                ["account_id"] = "acct-1",
                ["source_record_id"] = "doc-1",
                ["life_work_scope"] = "work",
                ["sensitivity_label"] = "private_work",
                ["candidate_memory_domains"] = "business",
                ["candidate_memory_types"] = "decision_memory,project_memory",
                ["confidence"] = 0.88,
                ["source_sha256"] = "sha-3"
            });

        var projection = builder.Build(request, "trace-3");

        projection.Nodes.Should().Contain(node => node.Labels.Contains("Decision"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("Project"));
        projection.Edges.Should().Contain(edge => edge.Relation == "AFFECTS");
    }

    private static IngestDocumentRequestDto BuildConnectorRequest(
        string recordId,
        string content,
        Dictionary<string, object?> metadata)
    {
        return new IngestDocumentRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            Tags = new List<string> { "connector_normalized", "connector_normalized_v1" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = recordId,
                Title = recordId,
                Content = content,
                ContentEncoding = "plain",
                MimeType = "application/json",
                Source = "connector_normalized",
                CreatedUtc = DateTimeOffset.Parse("2026-04-29T15:00:00Z"),
                Metadata = metadata
            }
        };
    }
}
