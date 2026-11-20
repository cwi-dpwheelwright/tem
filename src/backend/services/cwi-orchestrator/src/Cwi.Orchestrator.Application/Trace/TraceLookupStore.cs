#nullable enable
using System.Collections.Concurrent;

namespace Cwi.Orchestrator.Application.Trace;

public sealed class TraceLookupStore
{
    private readonly ConcurrentDictionary<string, string> _traceToDoc = new();

    public void Record(string traceId, string docId)
    {
        if (string.IsNullOrWhiteSpace(traceId) || string.IsNullOrWhiteSpace(docId))
        {
            return;
        }

        _traceToDoc[traceId] = docId;
    }

    public string? TryGetDocId(string traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return null;
        }

        return _traceToDoc.TryGetValue(traceId, out var docId) ? docId : null;
    }
}
#nullable disable
