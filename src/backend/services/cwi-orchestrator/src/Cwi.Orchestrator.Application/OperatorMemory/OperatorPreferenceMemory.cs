#nullable enable
using System.Collections.Concurrent;

namespace Cwi.Orchestrator.Application.OperatorMemory;

public sealed class OperatorPreferenceMemory
{
    private readonly ConcurrentDictionary<string, OperatorPreferenceRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);

    public OperatorPreference Update(
        string tenantId,
        string operatorId,
        string preferenceKey,
        string preferenceValue,
        string sourceText)
    {
        var record = _records.GetOrAdd(
            BuildRecordKey(tenantId, operatorId),
            _ => new OperatorPreferenceRecord());

        lock (record.Gate)
        {
            var preference = new OperatorPreference(
                preferenceKey.Trim(),
                preferenceValue.Trim(),
                sourceText.Trim(),
                DateTimeOffset.UtcNow);
            record.Preferences[preference.Key] = preference;
            return preference;
        }
    }

    public OperatorPreference? Get(string tenantId, string operatorId, string preferenceKey)
    {
        if (!_records.TryGetValue(BuildRecordKey(tenantId, operatorId), out var record))
        {
            return null;
        }

        lock (record.Gate)
        {
            return record.Preferences.TryGetValue(preferenceKey.Trim(), out var preference)
                ? preference
                : null;
        }
    }

    private static string BuildRecordKey(string tenantId, string operatorId)
    {
        return $"{tenantId.Trim().ToLowerInvariant()}::{operatorId.Trim().ToLowerInvariant()}";
    }

    private sealed class OperatorPreferenceRecord
    {
        public object Gate { get; } = new();
        public Dictionary<string, OperatorPreference> Preferences { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record OperatorPreference(
    string Key,
    string Value,
    string SourceText,
    DateTimeOffset UpdatedUtc);
#nullable disable
