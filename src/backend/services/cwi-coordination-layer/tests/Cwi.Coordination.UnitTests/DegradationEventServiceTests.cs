using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.DataAccess;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class DegradationEventServiceTests
{
    [Fact]
    public async Task AppendAsync_Stores_Event()
    {
        var repo = new InMemoryDegradationEventRepository();
        var service = new DegradationEventService(repo);

        var request = new DegradationEventRequest
        {
            EventId = "event-1",
            NodeId = "node-1",
            ObservedUtc = DateTimeOffset.UtcNow,
            Severity = 0.5,
            Details = new Dictionary<string, object?> { ["load_1"] = 1.2 }
        };

        var response = await service.AppendAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data?.EventId.Should().Be("event-1");
    }

    [Fact]
    public async Task AppendAsync_Rejects_Invalid_Severity()
    {
        var repo = new InMemoryDegradationEventRepository();
        var service = new DegradationEventService(repo);

        var request = new DegradationEventRequest
        {
            EventId = "event-1",
            NodeId = "node-1",
            ObservedUtc = DateTimeOffset.UtcNow,
            Severity = 2.0
        };

        var response = await service.AppendAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
    }
}
