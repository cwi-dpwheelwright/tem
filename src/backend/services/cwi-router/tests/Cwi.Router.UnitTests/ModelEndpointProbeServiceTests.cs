using System.Reflection;
using Cwi.Router.Application.Health;
using FluentAssertions;
using Xunit;

namespace Cwi.Router.UnitTests;

public sealed class ModelEndpointProbeServiceTests
{
    [Fact]
    public void TryParseCapabilities_ValidPayload_ParsesFields()
    {
        var payload = """
        {
          "engine": "ollama",
          "tier": "pro",
          "ready": true,
          "capacity": { "max_concurrency": 4, "queue_depth": 16 },
          "inference_mode": "local"
        }
        """;

        var (ok, capabilities) = InvokeTryParse(payload, new Uri("http://localhost:11434"));

        ok.Should().BeTrue();
        capabilities.Engine.Should().Be("ollama");
        capabilities.Tier.Should().Be("pro");
        capabilities.Ready.Should().BeTrue();
        capabilities.MaxConcurrency.Should().Be(4);
        capabilities.QueueDepth.Should().Be(16);
        capabilities.InferenceMode.Should().Be(InferenceEngineMode.Local);
    }

    [Fact]
    public void TryParseCapabilities_InvalidPayload_FailsGracefully()
    {
        var (ok, _) = InvokeTryParse("not-json", new Uri("http://localhost:11434"));

        ok.Should().BeFalse();
    }

    private static (bool ok, ModelEndpointCapabilities capabilities) InvokeTryParse(string payload, Uri endpoint)
    {
        var method = typeof(ModelEndpointProbeService)
            .GetMethod("TryParseCapabilities", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { payload, endpoint, null };
        var result = method!.Invoke(null, args);
        result.Should().BeOfType<bool>();

        var ok = (bool)result!;
        var capabilities = args[2] as ModelEndpointCapabilities ?? new ModelEndpointCapabilities();
        return (ok, capabilities);
    }
}
