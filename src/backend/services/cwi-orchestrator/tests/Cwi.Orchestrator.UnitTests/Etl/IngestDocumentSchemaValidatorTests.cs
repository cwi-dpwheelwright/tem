#nullable enable
using Cwi.Orchestrator.Application.Etl;
using Cwi.Orchestrator.Application.Models;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Etl;

public sealed class IngestDocumentSchemaValidatorTests
{
    [Fact]
    public void Validate_ReturnsSuccess_ForValidRequest()
    {
        var validator = new IngestDocumentSchemaValidator();
        var request = BuildValidRequest();

        var result = validator.Validate(request);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ReturnsFailure_WhenTenantMissing()
    {
        var validator = new IngestDocumentSchemaValidator();
        var request = BuildValidRequest();
        request.TenantId = string.Empty;

        var result = validator.Validate(request);

        Assert.False(result.IsSuccess);
    }

    private static IngestDocumentRequestDto BuildValidRequest()
    {
        return new IngestDocumentRequestDto
        {
            Version = "v1",
            TenantId = "tenant-1",
            IdempotencyKey = "ingest-123",
            Tags = new List<string> { "manual" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = "doc-123",
                Title = "Checklist",
                MimeType = "text/plain",
                Content = "Checklist content",
                ContentEncoding = "plain",
                Source = "edge-agent",
                CreatedUtc = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, object?>
                {
                    ["file_name"] = "checklist.txt"
                }
            }
        };
    }
}
#nullable disable
