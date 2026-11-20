using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cwi.Orchestrator.Application.HomeNodes;

public sealed class HomeNodeWorkTokenService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationOptions _options;

    public HomeNodeWorkTokenService(ApplicationOptions options)
    {
        _options = options;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.HomeNodeWorkTokenSecret);

    public string CreateAskToken(
        string workId,
        string? requestId,
        string traceId,
        string tenantId,
        string userId,
        string nodeId,
        string? policyScope,
        string packName)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Home node work token signing secret is not configured.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresUtc = now.AddSeconds(_options.HomeNodeWorkTokenTtlSeconds);

        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["alg"] = "HS256",
            ["typ"] = "CWIWT"
        }, JsonOptions));

        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["iss"] = _options.HomeNodeWorkTokenIssuer,
            ["sub"] = userId,
            ["aud"] = nodeId,
            ["jti"] = workId,
            ["request_id"] = requestId,
            ["trace_id"] = traceId,
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["node_id"] = nodeId,
            ["policy_scope"] = policyScope,
            ["task_type"] = "ask.execute",
            ["pack_name"] = packName,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresUtc.ToUnixTimeSeconds()
        }, JsonOptions));

        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.HomeNodeWorkTokenSecret!));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        return $"{signingInput}.{signature}";
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
