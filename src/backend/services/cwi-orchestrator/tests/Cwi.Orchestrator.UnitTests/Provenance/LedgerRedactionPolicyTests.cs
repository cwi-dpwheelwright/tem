using Cwi.Orchestrator.Application.Provenance;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Provenance;

public sealed class LedgerRedactionPolicyTests
{
    [Fact]
    public void RedactPayload_RemovesConfiguredFields()
    {
        var policy = new LedgerRedactionPolicy(true, new[] { "token" });
        var payload = "{\"token\":\"secret\",\"value\":42}";

        var redacted = policy.RedactPayload(payload);

        Assert.Contains("[REDACTED]", redacted);
        Assert.DoesNotContain("secret", redacted);
    }
}
