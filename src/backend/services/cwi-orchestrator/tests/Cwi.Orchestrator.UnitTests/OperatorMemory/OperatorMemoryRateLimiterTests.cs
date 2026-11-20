#nullable enable
using Cwi.Orchestrator.Application.OperatorMemory;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.OperatorMemory;

public class OperatorMemoryRateLimiterTests
{
    [Fact]
    public void RateLimiter_rejects_when_window_exceeded()
    {
        var options = new OperatorMemoryOptions
        {
            MaxEventsPerWindow = 2,
            RateLimitWindow = System.TimeSpan.FromMinutes(1)
        };
        var limiter = new OperatorMemoryRateLimiter(options);

        limiter.TryConsume("tenant:operator", 2, out _).Should().BeTrue();
        limiter.TryConsume("tenant:operator", 1, out var retryAfter).Should().BeFalse();
        retryAfter.Should().NotBeNull();
        retryAfter!.Value.Should().BeGreaterThan(System.TimeSpan.Zero);
    }
}
#nullable disable
