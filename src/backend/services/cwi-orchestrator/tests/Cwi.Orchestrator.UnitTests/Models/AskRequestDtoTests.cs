using System.Text.Json;
using Cwi.Orchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Models;

public sealed class AskRequestDtoTests
{
    [Fact]
    public void Deserializes_trusted_web_retrieval_context()
    {
        const string json = """
            {
              "tenant_id": "tenant-1",
              "user_id": "user-1",
              "question": "what changed",
              "pack": { "name": "enterprise", "anchor": "hash-1" },
              "context": {
                "mode": "observe",
                "top_k": 3,
                "web_retrieval": {
                  "enabled": true,
                  "mode": "include",
                  "profile": "general",
                  "max_results": 4
                }
              }
            }
            """;

        var request = JsonSerializer.Deserialize<AskRequestDto>(json);

        request.Should().NotBeNull();
        request!.Context.WebRetrieval.Should().NotBeNull();
        request.Context.WebRetrieval!.Enabled.Should().BeTrue();
        request.Context.WebRetrieval.Mode.Should().Be("include");
        request.Context.WebRetrieval.Profile.Should().Be("general");
        request.Context.WebRetrieval.MaxResults.Should().Be(4);
    }
}
