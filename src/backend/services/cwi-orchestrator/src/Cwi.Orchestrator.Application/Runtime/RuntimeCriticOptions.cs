#nullable enable
using System;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class RuntimeCriticOptions
{
    public string? BaseUrl { get; init; }
    public string EndpointPath { get; init; } = "/runtime/critic/observe";
    public int TimeoutMs { get; init; } = 1500;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    public static RuntimeCriticOptions LoadFromEnvironment()
    {
        return new RuntimeCriticOptions
        {
            BaseUrl = NormalizeOptional(Environment.GetEnvironmentVariable("CWI_CRITIC_BASE_URL")),
            EndpointPath = NormalizePath(Environment.GetEnvironmentVariable("CWI_CRITIC_OBSERVE_PATH")),
            TimeoutMs = ReadPositiveInt("CWI_CRITIC_TIMEOUT_MS", 1500)
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/runtime/critic/observe";
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }

    private static int ReadPositiveInt(string key, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
#nullable disable
