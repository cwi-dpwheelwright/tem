using System.Text.Json;
using System.Text.Json.Serialization;
using Cwi.Common.Events;

namespace Cwi.Common.Journey;

public sealed record JourneyEvent
{
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("service")]
    public string Service { get; init; } = "unknown";

    [JsonPropertyName("component")]
    public string Component { get; init; } = string.Empty;

    [JsonPropertyName("phase")]
    public string Phase { get; init; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; init; } = "request";

    [JsonPropertyName("from_component")]
    public string? FromComponent { get; init; }

    [JsonPropertyName("to_component")]
    public string? ToComponent { get; init; }

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("pack_name")]
    public string? PackName { get; init; }

    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("status_code")]
    public int? StatusCode { get; init; }

    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class JourneyEventEnvelopeConverter
{
    public const string JourneyEventType = "journey.event";

    public static EventEnvelope ToEnvelope(this JourneyEvent journeyEvent, string? environment = null)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["request_id"] = journeyEvent.RequestId,
            ["trace_id"] = journeyEvent.TraceId,
            ["component"] = journeyEvent.Component,
            ["phase"] = journeyEvent.Phase,
            ["direction"] = journeyEvent.Direction,
            ["from_component"] = journeyEvent.FromComponent,
            ["to_component"] = journeyEvent.ToComponent,
            ["tenant_id"] = journeyEvent.TenantId,
            ["user_id"] = journeyEvent.UserId,
            ["pack_name"] = journeyEvent.PackName,
            ["method"] = journeyEvent.Method,
            ["path"] = journeyEvent.Path,
            ["status_code"] = journeyEvent.StatusCode,
            ["duration_ms"] = journeyEvent.DurationMs,
            ["created_utc"] = journeyEvent.CreatedUtc,
            ["metadata"] = journeyEvent.Metadata
        };

        return new EventEnvelope
        {
            EventId = journeyEvent.EventId,
            TimestampUtc = journeyEvent.CreatedUtc,
            Service = journeyEvent.Service,
            Environment = string.IsNullOrWhiteSpace(environment) ? "unknown" : environment,
            Type = JourneyEventType,
            Polarity = "positive",
            Severity = ResolveSeverity(journeyEvent.StatusCode),
            Message = journeyEvent.Message ?? $"{journeyEvent.Component}.{journeyEvent.Phase}",
            TenantId = journeyEvent.TenantId,
            TraceId = journeyEvent.TraceId,
            Data = data
        };
    }

    public static bool TryParse(EventEnvelope envelope, out JourneyEvent journeyEvent)
    {
        journeyEvent = default!;
        if (!string.Equals(envelope.Type, JourneyEventType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var requestId = ReadString(envelope.Data, "request_id");
        var component = ReadString(envelope.Data, "component");
        var phase = ReadString(envelope.Data, "phase");
        if (string.IsNullOrWhiteSpace(requestId) ||
            string.IsNullOrWhiteSpace(component) ||
            string.IsNullOrWhiteSpace(phase))
        {
            return false;
        }

        journeyEvent = new JourneyEvent
        {
            EventId = string.IsNullOrWhiteSpace(envelope.EventId) ? Guid.NewGuid().ToString("n") : envelope.EventId,
            RequestId = requestId,
            TraceId = ReadString(envelope.Data, "trace_id") ?? envelope.TraceId,
            Service = string.IsNullOrWhiteSpace(envelope.Service) ? "unknown" : envelope.Service,
            Component = component,
            Phase = phase,
            Direction = ReadString(envelope.Data, "direction") ?? "request",
            FromComponent = ReadString(envelope.Data, "from_component"),
            ToComponent = ReadString(envelope.Data, "to_component"),
            TenantId = ReadString(envelope.Data, "tenant_id") ?? envelope.TenantId,
            UserId = ReadString(envelope.Data, "user_id"),
            PackName = ReadString(envelope.Data, "pack_name"),
            Method = ReadString(envelope.Data, "method"),
            Path = ReadString(envelope.Data, "path"),
            StatusCode = ReadInt(envelope.Data, "status_code"),
            DurationMs = ReadLong(envelope.Data, "duration_ms"),
            Message = string.IsNullOrWhiteSpace(envelope.Message) ? null : envelope.Message,
            CreatedUtc = ReadDateTimeOffset(envelope.Data, "created_utc") ?? envelope.TimestampUtc,
            Metadata = ReadMetadata(envelope.Data, "metadata")
        };

        return true;
    }

    private static string ResolveSeverity(int? statusCode)
    {
        return statusCode is >= 400 ? "warn" : "info";
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => string.IsNullOrWhiteSpace(text) ? null : text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.ToString(),
            _ => value.ToString()
        };
    }

    private static int? ReadInt(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int number => number,
            long number => checked((int)number),
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) => number,
            JsonElement element when int.TryParse(element.ToString(), out var number) => number,
            _ when int.TryParse(value.ToString(), out var number) => number,
            _ => null
        };
    }

    private static long? ReadLong(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            long number => number,
            int number => number,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number) => number,
            JsonElement element when long.TryParse(element.ToString(), out var number) => number,
            _ when long.TryParse(value.ToString(), out var number) => number,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDateTimeOffset(IReadOnlyDictionary<string, object?> data, string key)
    {
        var value = ReadString(data, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static Dictionary<string, object?> ReadMetadata(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (value is Dictionary<string, object?> metadata)
        {
            return new Dictionary<string, object?>(metadata, StringComparer.OrdinalIgnoreCase);
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText()) ??
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }
}
