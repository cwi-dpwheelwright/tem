using Cwi.Router.Application.Routing;
using FluentAssertions;
using Xunit;

namespace Cwi.Router.UnitTests;

public sealed class NetworkAssessmentTests
{
    [Fact]
    public void ScoresBestInterfaceAndStatus()
    {
        var options = new NetworkAssessmentOptions
        {
            HealthyThreshold = 0.7,
            DegradedThreshold = 0.4
        };
        var scorer = new NetworkAssessmentScorer(options);

        var input = new NetworkAssessmentInput
        {
            Interfaces = new[]
            {
                new NetworkInterfaceSignal
                {
                    Interface = "lte",
                    State = "up",
                    SignalQuality = 0.9,
                    PacketLoss = 0.01,
                    LatencyMs = 30,
                    ThroughputMbps = 25
                },
                new NetworkInterfaceSignal
                {
                    Interface = "sat",
                    State = "degraded",
                    SignalQuality = 0.4,
                    PacketLoss = 0.12,
                    LatencyMs = 400,
                    ThroughputMbps = 4
                }
            }
        };

        var score = scorer.Score(input);

        score.PreferredInterface.Should().Be("lte");
        score.Status.Should().Be("healthy");
        score.Score.Should().NotBeNull();
        score.Score.Should().BeGreaterThan(0.7);
    }

    [Fact]
    public void ParsesMultiInterfaceNetworkAssessment()
    {
        var body = "{\"network_assessment\":{\"node_id\":\"node-9\",\"observed_utc\":\"2026-02-02T12:00:00Z\",\"interfaces\":[{\"interface\":\"lte\",\"state\":\"up\",\"signal_quality\":0.82,\"latency_ms\":30,\"jitter_ms\":4,\"packet_loss\":0.01,\"throughput_mbps\":18.5},{\"interface\":\"sat\",\"state\":\"degraded\",\"signal_quality\":0.41,\"latency_ms\":420,\"packet_loss\":0.1,\"throughput_mbps\":3.9}]}}";

        var parsed = NetworkAssessmentParser.TryParse(body, out var input);

        parsed.Should().BeTrue();
        input.NodeId.Should().Be("node-9");
        input.Interfaces.Should().HaveCount(2);
        input.Interfaces[0].Interface.Should().Be("lte");
        input.Interfaces[1].Interface.Should().Be("sat");
    }
}
