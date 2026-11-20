#nullable enable
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoreLanguageNormalizationResult = Cwi.Normalization.Core.LanguageNormalizationResult;

namespace Cwi.Orchestrator.Application.Ask;

public interface ILanguageNormalizationClient
{
    Task<LanguageNormalizationResult> NormalizeAsync(string? prompt, CancellationToken cancellationToken);
}

public sealed class LocalLanguageNormalizationClient : ILanguageNormalizationClient
{
    public Task<LanguageNormalizationResult> NormalizeAsync(string? prompt, CancellationToken cancellationToken)
    {
        return Task.FromResult(LanguageNormalizer.Normalize(prompt));
    }
}

public sealed class HttpLanguageNormalizationClient : ILanguageNormalizationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _client;

    public HttpLanguageNormalizationClient(HttpClient client)
    {
        _client = client;
    }

    public async Task<LanguageNormalizationResult> NormalizeAsync(string? prompt, CancellationToken cancellationToken)
    {
        var raw = prompt ?? string.Empty;
        if (_client.BaseAddress is null)
        {
            return BuildFailure(raw, "normalization_base_url_missing");
        }

        try
        {
            using var response = await _client.PostAsJsonAsync(
                "api/normalization/normalize",
                new NormalizationRequest(raw),
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return BuildFailure(raw, $"normalization_http_{(int)response.StatusCode}");
            }

            var result = await response.Content
                .ReadFromJsonAsync<CoreLanguageNormalizationResult>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result is null
                ? BuildFailure(raw, "normalization_response_empty")
                : LanguageNormalizer.FromCore(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return BuildFailure(raw, ex.GetType().Name);
        }
    }

    private static LanguageNormalizationResult BuildFailure(string raw, string reason)
    {
        return new LanguageNormalizationResult(
            raw,
            string.Empty,
            false,
            new[] { "external_normalization_failed" },
            Failed: true,
            FailureReason: reason,
            Confidence: 0.0f);
    }

    private sealed record NormalizationRequest(string? Prompt);
}

public sealed class FallbackLanguageNormalizationClient : ILanguageNormalizationClient
{
    private readonly HttpLanguageNormalizationClient _external;
    private readonly LocalLanguageNormalizationClient _local;

    public FallbackLanguageNormalizationClient(
        HttpLanguageNormalizationClient external,
        LocalLanguageNormalizationClient local)
    {
        _external = external;
        _local = local;
    }

    public async Task<LanguageNormalizationResult> NormalizeAsync(string? prompt, CancellationToken cancellationToken)
    {
        var externalResult = await _external.NormalizeAsync(prompt, cancellationToken).ConfigureAwait(false);
        if (!externalResult.Failed)
        {
            return externalResult;
        }

        var localResult = await _local.NormalizeAsync(prompt, cancellationToken).ConfigureAwait(false);
        return localResult with
        {
            AppliedRules = localResult.AppliedRules
                .Concat(new[] { "external_normalizer_failed_local_fallback" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}
#nullable disable
