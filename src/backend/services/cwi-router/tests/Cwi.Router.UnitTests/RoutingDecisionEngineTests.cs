using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Routing;
using Cwi.Router.Application.Survival;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Router.UnitTests;

[Collection("Env")]
public sealed class RoutingDecisionEngineTests
{
    [Fact]
    public void SurvivalMode_ForcesFreeTier()
    {
        ConfigureEnvironment(modelProUrl: "http://localhost:11434", modelFreeUrl: "http://localhost:11435");
        var config = RouterConfig.LoadFromEnvironment();
        var registry = new ModelEndpointRegistry(config, new ModelEndpointCircuitBreaker(), NullLogger<ModelEndpointRegistry>.Instance);
        var survivalStore = new SurvivalStateStore();
        survivalStore.Update("survival", "test");
        var engine = new RoutingDecisionEngine(registry, survivalStore, new NetworkAssessmentOptions { Enabled = false });

        var descriptor = new ComplexityDescriptor
        {
            RequestKind = "ask",
            WordCount = 300,
            CharacterCount = 1500,
            PayloadBytes = 1000,
            Level = ComplexityLevel.High,
            Score = 0.9
        };

        var decision = engine.Decide(descriptor, ModelTier.Pro, DateTimeOffset.UtcNow, null);

        decision.SelectedTier.Should().Be(ModelTier.Free);
        decision.ReasonCodes.Should().Contain("survival_mode_bias");
    }

    [Fact]
    public void FallsBackWhenPreferredTierUnavailable()
    {
        ConfigureEnvironment(modelProUrl: string.Empty, modelFreeUrl: "http://localhost:11435");
        var config = RouterConfig.LoadFromEnvironment();
        var registry = new ModelEndpointRegistry(config, new ModelEndpointCircuitBreaker(), NullLogger<ModelEndpointRegistry>.Instance);
        var survivalStore = new SurvivalStateStore();
        var engine = new RoutingDecisionEngine(registry, survivalStore, new NetworkAssessmentOptions { Enabled = false });

        var descriptor = new ComplexityDescriptor
        {
            RequestKind = "ask",
            WordCount = 10,
            CharacterCount = 50,
            PayloadBytes = 200,
            Level = ComplexityLevel.High,
            Score = 0.9
        };

        var decision = engine.Decide(descriptor, ModelTier.Pro, DateTimeOffset.UtcNow, null);

        decision.SelectedTier.Should().Be(ModelTier.Free);
        decision.FallbackUsed.Should().BeTrue();
    }

    [Fact]
    public void AppliesCapabilityBiasToScore()
    {
        ConfigureEnvironment(modelProUrl: "http://localhost:11434", modelFreeUrl: "http://localhost:11435");
        var config = RouterConfig.LoadFromEnvironment();
        var registry = new ModelEndpointRegistry(config, new ModelEndpointCircuitBreaker(), NullLogger<ModelEndpointRegistry>.Instance);
        var endpoint = registry.GetEndpoints(ModelTier.Pro)[0];
        registry.RecordCapabilities(endpoint, new ModelEndpointCapabilities
        {
            Engine = "ollama",
            Tier = "pro",
            Ready = true,
            InferenceMode = InferenceEngineMode.Local
        });
        var engine = new RoutingDecisionEngine(registry, new SurvivalStateStore(), new NetworkAssessmentOptions { Enabled = false });

        var descriptor = new ComplexityDescriptor
        {
            RequestKind = "ask",
            WordCount = 10,
            CharacterCount = 50,
            PayloadBytes = 200,
            Level = ComplexityLevel.Medium,
            Score = 0.5
        };

        var decision = engine.Decide(descriptor, ModelTier.Pro, DateTimeOffset.UtcNow, null);

        decision.Score.Should().BeLessThan(0.5);
        decision.ReasonCodes.Should().Contain("capability_local_inference");
        decision.EndpointCapabilities.Should().NotBeNull();
    }

    [Fact]
    public void AppliesNetworkBiasAndRerouteSignals()
    {
        ConfigureEnvironment(modelProUrl: "http://localhost:11434", modelFreeUrl: "http://localhost:11435");
        var config = RouterConfig.LoadFromEnvironment();
        var registry = new ModelEndpointRegistry(config, new ModelEndpointCircuitBreaker(), NullLogger<ModelEndpointRegistry>.Instance);
        var options = new NetworkAssessmentOptions
        {
            Enabled = true,
            ScoreWeight = 0.5,
            HealthyThreshold = 0.7,
            DegradedThreshold = 0.4,
            TriggerRerouteOnDegraded = true
        };
        var engine = new RoutingDecisionEngine(registry, new SurvivalStateStore(), options);

        var descriptor = new ComplexityDescriptor
        {
            RequestKind = "ask",
            WordCount = 10,
            CharacterCount = 50,
            PayloadBytes = 200,
            Level = ComplexityLevel.High,
            Score = 0.9
        };

        var assessment = new NetworkAssessmentSnapshot
        {
            Status = "degraded",
            Score = 0.2,
            RerouteRequired = true,
            RerouteReason = "network_degraded"
        };

        var decision = engine.Decide(descriptor, ModelTier.Pro, DateTimeOffset.UtcNow, assessment);

        decision.Score.Should().BeLessThan(0.9);
        decision.RerouteRequired.Should().BeTrue();
        decision.ReasonCodes.Should().Contain("network_degraded");
        decision.ReasonCodes.Should().Contain("reroute_required");
    }

    private static void ConfigureEnvironment(string modelProUrl, string modelFreeUrl)
    {
        Environment.SetEnvironmentVariable("ROUTER_HTTP_PORT", "8081");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://localhost:8080");
        Environment.SetEnvironmentVariable("AUTH_REQUIRED", "false");
        Environment.SetEnvironmentVariable("MTLS_REQUIRED", "false");
        Environment.SetEnvironmentVariable("REDIS_URL", string.Empty);
        Environment.SetEnvironmentVariable("CWI_RATE_LIMIT", "0");
        Environment.SetEnvironmentVariable("CWI_RATE_WINDOW_SECONDS", "60");
        Environment.SetEnvironmentVariable("MODEL_PRO_URL", modelProUrl);
        Environment.SetEnvironmentVariable("MODEL_FREE_URL", modelFreeUrl);
        Environment.SetEnvironmentVariable("MODEL_HEALTH_PATH", "/api/tags");
        Environment.SetEnvironmentVariable("MODEL_CB_FAILURE_THRESHOLD", "3");
        Environment.SetEnvironmentVariable("MODEL_CB_COOLDOWN_SECONDS", "30");
        Environment.SetEnvironmentVariable("CWI_POLICY_MODE_DEFAULT", "standard");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_TIMEOUT_SECONDS", "5");
    }
}
