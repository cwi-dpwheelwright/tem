#nullable enable
namespace Cwi.Orchestrator.Application.Memory;

public sealed class ActivatedMemoryOptions
{
    public string ActivatedDirectory { get; init; } = "/srv/cwi/memory/activated";
    public string ActivationLedgerPath { get; init; } = "/srv/cwi/ledger/memory_activation_events.jsonl";

    public static ActivatedMemoryOptions LoadFromEnvironment()
    {
        return new ActivatedMemoryOptions
        {
            ActivatedDirectory = ReadString("CWI_ACTIVATED_MEMORY_DIR", "/srv/cwi/memory/activated"),
            ActivationLedgerPath = ReadString("CWI_MEMORY_ACTIVATION_LEDGER_PATH", "/srv/cwi/ledger/memory_activation_events.jsonl")
        };
    }

    private static string ReadString(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
#nullable disable
