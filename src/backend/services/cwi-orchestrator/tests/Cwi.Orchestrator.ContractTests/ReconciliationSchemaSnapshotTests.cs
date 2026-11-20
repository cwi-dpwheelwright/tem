using System.Text;
using System.Text.Json;
using Cwi.Orchestrator.Application.Reconciliation;
using Xunit;

namespace Cwi.Orchestrator.ContractTests;

public sealed class ReconciliationSchemaSnapshotTests
{
    [Fact]
    public void CanonicalRecordSchema_Snapshot()
    {
        AssertSchema("canonical_record.json");
    }

    [Fact]
    public void SourceMetadataSchema_Snapshot()
    {
        AssertSchema("source_metadata.json");
    }

    private static void AssertSchema(string filename)
    {
        var path = Path.Combine(ResolveSchemaRoot(), filename);
        var json = File.ReadAllText(path, Encoding.UTF8);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.NotEqual(default, element);
    }

    private static string ResolveSchemaRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, "contracts", "json-schema");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate contracts/json-schema.");
    }
}
