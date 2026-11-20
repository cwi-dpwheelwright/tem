#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace Cwi.Orchestrator.Application.TrustedWeb;

internal static class TrustedWebEvidence
{
    public const string SourceType = "trusted_web";
    public const string TrustTier = "configured_trusted_site";

    public static string BuildEvidenceId(TrustedWebResult result)
    {
        return BuildEvidenceId(result.SourceId, result.ChunkId);
    }

    public static string BuildEvidenceId(string sourceId, string chunkId)
    {
        var resolvedSourceId = string.IsNullOrWhiteSpace(sourceId) ? "source" : sourceId.Trim();
        var resolvedChunkId = string.IsNullOrWhiteSpace(chunkId) ? "chunk" : chunkId.Trim();
        return $"web:{resolvedSourceId}:{resolvedChunkId}";
    }

    public static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
#nullable disable
