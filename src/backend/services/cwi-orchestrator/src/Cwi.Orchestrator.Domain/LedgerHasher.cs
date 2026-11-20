using System.Security.Cryptography;
using System.Text;

namespace Cwi.Orchestrator.Domain;

public static class LedgerHasher
{
    public const string DefaultSuite = "sha256-v1";
    public const string SuiteV1 = "sha256-v1";
    public const string SuiteV2 = "sha256-v2";

    public static string ComputeHash(
        string prevHash,
        string traceId,
        string eventType,
        string payloadJson,
        DateTimeOffset createdUtc,
        string? suiteId = null)
    {
        var resolvedSuite = ResolveSuite(suiteId);
        var builder = new StringBuilder();
        if (resolvedSuite == SuiteV2)
        {
            builder.Append(resolvedSuite);
            builder.Append('|');
        }

        builder.Append(prevHash ?? string.Empty);
        builder.Append('|');
        builder.Append(traceId ?? string.Empty);
        builder.Append('|');
        builder.Append(eventType ?? string.Empty);
        builder.Append('|');
        builder.Append(payloadJson ?? string.Empty);
        builder.Append('|');
        builder.Append(createdUtc.ToUnixTimeMilliseconds());

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ResolveSuite(string? suiteId)
    {
        if (string.IsNullOrWhiteSpace(suiteId))
        {
            return DefaultSuite;
        }

        return suiteId.Trim().ToLowerInvariant() switch
        {
            SuiteV1 => SuiteV1,
            SuiteV2 => SuiteV2,
            _ => DefaultSuite
        };
    }
}
