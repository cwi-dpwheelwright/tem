using Cwi.Common.Events;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Application.Journey;

namespace Cwi.Orchestrator.UnitTests;

internal static class JourneyTestHelpers
{
    public static JourneyEmitter CreateEmitter(string requestId = "request-1")
    {
        return new JourneyEmitter(CreateContext(requestId), CreatePublisher());
    }

    public static JourneyRequestContext CreateContext(string requestId = "request-1")
    {
        return new JourneyRequestContext
        {
            RequestId = requestId,
            TraceId = "trace-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            PackName = "pack-1",
            Method = "POST",
            Path = "/api/ask"
        };
    }

    private static JourneyEventBusPublisher CreatePublisher()
    {
        var options = new EventBusClientOptions
        {
            Enabled = false,
            ServiceName = "cwi-orchestrator",
            Environment = "test"
        };
        return new JourneyEventBusPublisher(new EventBusClient(new HttpClient(), options), options);
    }
}
