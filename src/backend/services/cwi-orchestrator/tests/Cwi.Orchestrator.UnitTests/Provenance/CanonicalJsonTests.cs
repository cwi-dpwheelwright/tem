using System.Text.Json;
using Cwi.Orchestrator.Application.Provenance;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Provenance;

public class CanonicalJsonTests
{
    [Fact]
    public void Canonicalize_sorts_object_properties()
    {
        using var doc = JsonDocument.Parse("{\"b\":1,\"a\":2}");

        var canonical = CanonicalJson.Canonicalize(doc.RootElement);

        canonical.Should().Be("{\"a\":2,\"b\":1}");
    }
}
