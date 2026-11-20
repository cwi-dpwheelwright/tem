using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Graph;

public sealed class ImportedChatGraphProjectionBuilderTests
{
    [Fact]
    public void Build_projects_chat_history_into_user_memory_graph_facts()
    {
        var builder = new ImportedChatGraphProjectionBuilder();
        var request = new IngestDocumentRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            Tags = new List<string> { "gpt-web-ui", "chat-history-import", "chat_history" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = "chat-export-1",
                Title = "Chat Export",
                Content = """
                    User: I have been working on the CWI graph memory runtime.
                    Assistant: The next action is to wire ChatGPT import storage into AGE graph ingestion.
                    User: My home routine includes school pickup and house chores.
                    User: My goal is to finish the investor demo.
                    """,
                ContentEncoding = "plain",
                MimeType = "text/plain",
                Source = "cwi-gpt-web-ui",
                Metadata = new Dictionary<string, object?>
                {
                    ["upload_batch_id"] = "batch-1",
                    ["import_kind"] = "chat_history"
                }
            }
        };

        var projection = builder.Build(request, "trace-1");

        projection.Nodes.Should().Contain(node => node.Labels.Contains("user"));
        projection.Nodes.Should().Contain(node => node.Labels.Contains("chat_import_source"));
        projection.Nodes
            .Where(node => node.Labels.Contains("memory_fact"))
            .Should()
            .Contain(node => node.Properties["memory_category"]!.Equals("current_work"));
        projection.Nodes
            .Where(node => node.Labels.Contains("memory_fact"))
            .Should()
            .Contain(node => node.Properties["memory_category"]!.Equals("open_loop"));
        projection.Nodes
            .Where(node => node.Labels.Contains("memory_fact"))
            .Should()
            .Contain(node => node.Properties["memory_domain"]!.Equals("personal"));
        projection.Edges.Should().Contain(edge => edge.Relation == "HAS_MEMORY");
        projection.Edges.Should().Contain(edge => edge.Relation == "CONTAINS_MEMORY");
    }

    [Fact]
    public void Build_excludes_chat_export_application_metadata_from_memory_facts()
    {
        var builder = new ImportedChatGraphProjectionBuilder();
        var request = new IngestDocumentRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            Tags = new List<string> { "chat-history-import", "chat_history" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = "chat-export-technical",
                Title = "Chat Export",
                Content = """
                    "oai/apps/hasSeenProjectsUpdateNuxTooltip": "2025-06-14T20:09:39.333338+00:00",
                    User: I have been working on the CWI graph memory runtime.
                    """,
                ContentEncoding = "plain",
                MimeType = "text/plain",
                Source = "cwi-gpt-web-ui",
                Metadata = new Dictionary<string, object?>
                {
                    ["import_kind"] = "chat_history"
                }
            }
        };

        var projection = builder.Build(request, "trace-1");

        var factTexts = projection.Nodes
            .Where(node => node.Labels.Contains("memory_fact"))
            .Select(node => node.Properties["fact_text"]?.ToString() ?? string.Empty)
            .ToArray();

        factTexts.Should().NotContain(text => text.Contains("oai/apps/", StringComparison.OrdinalIgnoreCase));
        factTexts.Should().Contain(text => text.Contains("CWI graph memory runtime", StringComparison.OrdinalIgnoreCase));
    }
}
