using System.Text.Json;

namespace Cwi.Router.Application.Routing;

public static class NetworkAssessmentParser
{
    public static bool TryParse(string? body, out NetworkAssessmentInput input)
    {
        input = new NetworkAssessmentInput();
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        if (!TryParseJson(body, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("network_assessment", out var assessmentElement) ||
                assessmentElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var nodeId = GetString(assessmentElement, "node_id");
            var observedUtc = GetDateTime(assessmentElement, "observed_utc");
            var interfaces = new List<NetworkInterfaceSignal>();

            if (assessmentElement.TryGetProperty("interfaces", out var interfacesElement) &&
                interfacesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in interfacesElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = GetString(item, "interface") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    interfaces.Add(new NetworkInterfaceSignal
                    {
                        Interface = name,
                        State = GetString(item, "state"),
                        SignalQuality = GetDouble(item, "signal_quality"),
                        LatencyMs = GetDouble(item, "latency_ms"),
                        JitterMs = GetDouble(item, "jitter_ms"),
                        PacketLoss = GetDouble(item, "packet_loss"),
                        ThroughputMbps = GetDouble(item, "throughput_mbps"),
                        BandwidthMbps = GetDouble(item, "bandwidth_mbps"),
                        RssiDbm = GetDouble(item, "rssi_dbm"),
                        SnrDb = GetDouble(item, "snr_db")
                    });
                }
            }

            input = new NetworkAssessmentInput
            {
                NodeId = nodeId,
                ObservedUtc = observedUtc,
                Interfaces = interfaces
            };
            return true;
        }
    }

    private static bool TryParseJson(string body, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var child) ||
            child.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return child.GetString();
    }

    private static double? GetDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var child))
        {
            return null;
        }

        if (child.ValueKind == JsonValueKind.Number && child.TryGetDouble(out var value))
        {
            return value;
        }

        if (child.ValueKind == JsonValueKind.String && double.TryParse(child.GetString(), out value))
        {
            return value;
        }

        return null;
    }

    private static DateTimeOffset? GetDateTime(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var child))
        {
            return null;
        }

        if (child.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(child.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
