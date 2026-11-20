using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cwi.Normalization.Api.Tests;

public sealed class NormalizationApiTests
{
    [Fact]
    public async Task Health_returns_service_status()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("cwi-language-normalization");
        body.Should().Contain("cwi-rule-normalizer-v1");
    }

    [Fact]
    public async Task Swagger_endpoint_is_enabled_by_default()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dev/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("info").GetProperty("title").GetString()
            .Should().Be("CWI Language Normalization API");
        body.Should().Contain("/api/normalization/normalize");
    }

    [Fact]
    public async Task Normalize_endpoint_returns_snake_case_normalization_contract()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/normalization/normalize", new { prompt = "capitol texas" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.GetProperty("normalized_prompt").GetString().Should().Be("capitol texas");
        root.GetProperty("requires_verification").GetBoolean().Should().BeTrue();
        root.GetProperty("confidence").GetSingle().Should().BeLessThan(0.85f);
        root.GetProperty("correction_decision").GetString().Should().Be("verification_required");
        root.GetProperty("correction_risk").GetString().Should().Be("ambiguous_meaning");
        root.GetProperty("correction_visibility").GetString().Should().Be("clarify");
        root.GetProperty("corrections").EnumerateArray().Should().ContainSingle();
        root.GetProperty("verification_message").GetString().Should().Be("Did you mean \"capital of Texas\" or \"Texas State Capitol\"?");
        root.GetProperty("engine_version").GetString().Should().Be("cwi-rule-normalizer-v1");
    }
}
