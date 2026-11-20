using System.Net;
using System.Text;
using Cwi.Orchestrator.Application.Ask;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ask;

public sealed class LanguageNormalizationClientTests
{
    [Fact]
    public async Task Http_client_posts_prompt_to_external_normalization_service()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "raw_prompt": "capitol texas",
                  "normalized_prompt": "capitol texas",
                  "applied": true,
                  "applied_rules": ["semantic_spelling_verification"],
                  "failed": false,
                  "requires_verification": true,
                  "verification_message": "Did you mean \"capital of Texas\" or \"Texas State Capitol\"?",
                  "verification_candidates": ["capital of Texas", "Texas State Capitol"],
                  "confidence": 0.58,
                  "engine_version": "replacement-normalizer-v1",
                  "correction_decision": "verification_required",
                  "correction_risk": "ambiguous_meaning",
                  "correction_visibility": "clarify",
                  "corrections": [
                    {
                      "original": "capitol",
                      "normalized": "capital of Texas | Texas State Capitol",
                      "category": "semantic_spelling_ambiguity",
                      "visibility": "clarify",
                      "reason": "correction_changes_possible_meaning"
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        }))
        {
            BaseAddress = new Uri("http://normalization.local/")
        };
        var client = new HttpLanguageNormalizationClient(http);

        var result = await client.NormalizeAsync("capitol texas", CancellationToken.None);

        result.Failed.Should().BeFalse();
        result.NormalizedPrompt.Should().Be("capitol texas");
        result.RequiresVerification.Should().BeTrue();
        result.Confidence.Should().Be(0.58f);
        result.VerificationMessage.Should().Be("Did you mean \"capital of Texas\" or \"Texas State Capitol\"?");
        result.CorrectionDecision.Should().Be("verification_required");
        result.CorrectionRisk.Should().Be("ambiguous_meaning");
        result.CorrectionVisibility.Should().Be("clarify");
        result.Corrections.Should().ContainSingle(correction => correction.Visibility == "clarify");
        result.EngineVersion.Should().Be("replacement-normalizer-v1");
    }

    [Fact]
    public async Task Http_client_returns_failed_normalization_when_external_service_is_unavailable()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        {
            BaseAddress = new Uri("http://normalization.local/")
        };
        var client = new HttpLanguageNormalizationClient(http);

        var result = await client.NormalizeAsync("what is cwi", CancellationToken.None);

        result.Failed.Should().BeTrue();
        result.FailureReason.Should().Be("normalization_http_503");
        result.Confidence.Should().Be(0.0f);
        result.AppliedRules.Should().Contain("external_normalization_failed");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/api/normalization/normalize");
            return Task.FromResult(_handler(request));
        }
    }
}
