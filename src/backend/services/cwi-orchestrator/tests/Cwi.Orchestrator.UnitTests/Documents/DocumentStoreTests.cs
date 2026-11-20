#nullable enable
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Documents;

public sealed class DocumentStoreTests
{
    [Fact]
    public void List_ReturnsSharedAndCurrentUserDocumentsOnly()
    {
        var store = new DocumentStore();
        store.RecordIngest(BuildRequest("tenant-1", "shared-doc", userId: null));
        store.RecordIngest(BuildRequest("tenant-1", "user-a-doc", userId: "user-a"));
        store.RecordIngest(BuildRequest("tenant-1", "user-b-doc", userId: "user-b"));

        var sharedOnly = store.List("tenant-1");
        var userADocuments = store.List("tenant-1", "user-a");

        sharedOnly.Select(item => item.DocId).Should().BeEquivalentTo(["shared-doc"]);
        userADocuments.Select(item => item.DocId).Should().BeEquivalentTo(["shared-doc", "user-a-doc"]);
    }

    [Fact]
    public void Get_HidesUserOwnedDocumentsFromOtherUsers()
    {
        var store = new DocumentStore();
        store.RecordIngest(BuildRequest("tenant-1", "user-a-doc", userId: "user-a"));

        var ownerView = store.Get("tenant-1", "user-a-doc", "user-a");
        var otherUserView = store.Get("tenant-1", "user-a-doc", "user-b");
        var sharedView = store.Get("tenant-1", "user-a-doc");

        ownerView.Should().NotBeNull();
        otherUserView.Should().BeNull();
        sharedView.Should().BeNull();
    }

    [Fact]
    public void Rollback_RequiresAccessToTheOwnedDocument()
    {
        var store = new DocumentStore();
        store.RecordIngest(BuildRequest("tenant-1", "user-a-doc", userId: "user-a"));

        var denied = store.Rollback("tenant-1", "user-a-doc", "user-b", new DocumentRollbackRequestDto
        {
            TenantId = "tenant-1",
            ActorId = "user-b",
            TargetVersion = "v1",
        });

        denied.Should().BeNull();
    }

    [Fact]
    public void Search_ReturnsRelevantSharedAndCurrentUserDocumentsOnly()
    {
        var store = new DocumentStore();
        store.RecordIngest(BuildRequest(
            "tenant-1",
            "shared-redis-runbook",
            userId: null,
            content: "Redis latency increased after the deploy. Restart the worker pool only after cache health is stable."));
        store.RecordIngest(BuildRequest(
            "tenant-1",
            "user-a-redis-note",
            userId: "user-a",
            content: "User A observed Redis timeout spikes during the release window."));
        store.RecordIngest(BuildRequest(
            "tenant-1",
            "user-b-redis-note",
            userId: "user-b",
            content: "User B private Redis credential rotation notes."));
        store.RecordIngest(BuildRequest(
            "tenant-1",
            "button-copy",
            userId: "user-a",
            content: "Use direct action copy for upload buttons."));

        var results = store.Search("tenant-1", "user-a", "What happened with Redis latency?", topK: 10);

        results.Select(result => result.DocId).Should().Contain(["shared-redis-runbook", "user-a-redis-note"]);
        results.Select(result => result.DocId).Should().NotContain(["user-b-redis-note", "button-copy"]);
    }

    private static IngestDocumentRequestDto BuildRequest(
        string tenantId,
        string docId,
        string? userId,
        string? content = null)
    {
        return new IngestDocumentRequestDto
        {
            Version = "v1",
            TenantId = tenantId,
            UserId = userId,
            IdempotencyKey = $"{tenantId}:{docId}",
            Document = new IngestDocumentPayloadDto
            {
                DocId = docId,
                Title = docId,
                MimeType = "text/plain",
                Content = content ?? $"content for {docId}",
                ContentEncoding = "plain",
                Source = "cwi-gpt-web-ui",
                CreatedUtc = DateTimeOffset.Parse("2026-04-03T12:00:00Z"),
                Metadata = new Dictionary<string, object?>
                {
                    ["author"] = userId ?? "system"
                }
            }
        };
    }
}
#nullable disable
