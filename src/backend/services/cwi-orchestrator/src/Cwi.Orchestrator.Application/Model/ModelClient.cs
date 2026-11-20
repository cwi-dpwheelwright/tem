#nullable enable
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Model;

public sealed record ModelResult(string Model, string Output);

public sealed record ModelRequest(string Prompt, string? RequestedModel, string? RequestedTier);

public sealed record ModelTarget(string BaseUrl, string ModelName, string? SelectedTier);

public interface IModelClient
{
    Task<ServiceResponse<ModelResult>> GenerateAsync(ModelRequest request, CancellationToken cancellationToken);
}

public sealed class ModelTargetResolver
{
    private readonly ApplicationOptions _options;

    public ModelTargetResolver(ApplicationOptions options)
    {
        _options = options;
    }

    public ModelTarget Resolve(ModelRequest request)
    {
        var tier = NormalizeTier(request.RequestedTier);
        var requestedModel = string.IsNullOrWhiteSpace(request.RequestedModel) ? null : request.RequestedModel;

        var modelName = requestedModel ??
            tier switch
            {
                "pro" => _options.ModelNamePro,
                "free" => _options.ModelNameFree,
                _ => null
            } ??
            _options.ModelName;

        var baseUrl = tier switch
        {
            "pro" => _options.ModelBaseUrlPro,
            "free" => _options.ModelBaseUrlFree,
            _ => null
        } ?? _options.ModelBaseUrl;

        return new ModelTarget(baseUrl, modelName, tier);
    }

    private static string? NormalizeTier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pro" => "pro",
            "free" => "free",
            "edge" => "edge",
            _ => null
        };
    }
}

public sealed class OllamaModelClient : IModelClient
{
    private readonly ApplicationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ModelTargetResolver _resolver;

    public OllamaModelClient(ApplicationOptions options, IHttpClientFactory httpClientFactory, ModelTargetResolver resolver)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
    }

    public async Task<ServiceResponse<ModelResult>> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return ServiceResponse<ModelResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Prompt is required."),
                statusCode: 400,
                message: "Prompt required");
        }

        var target = _resolver.Resolve(request);
        var httpClient = _httpClientFactory.CreateClient("model");
        httpClient.BaseAddress = new Uri(target.BaseUrl);
        httpClient.Timeout = TimeSpan.FromSeconds(_options.ModelTimeoutSeconds);

        var ollamaRequest = new OllamaGenerateRequest
        {
            Model = target.ModelName,
            Prompt = request.Prompt,
            Stream = false,
            Options = new OllamaGenerateOptions
            {
                NumPredict = _options.ModelMaxOutputTokens,
                Temperature = 0.2
            }
        };

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await httpClient
                    .PostAsJsonAsync("api/generate", ollamaRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode >= 500 && attempt < 2)
                    {
                        continue;
                    }

                    return ServiceResponse<ModelResult>.Fail(
                        new DomainError(ErrorCodes.DependencyUnavailable, "Model inference failed."),
                        statusCode: (int)response.StatusCode,
                        message: "Model inference error");
                }

                var payload = await response.Content
                    .ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (payload is null)
                {
                    return ServiceResponse<ModelResult>.Fail(
                        new DomainError(ErrorCodes.DependencyUnavailable, "Model response empty."),
                        statusCode: 502,
                        message: "Model inference error");
                }

                return ServiceResponse<ModelResult>.Success(new ModelResult(
                    payload.Model ?? target.ModelName,
                    payload.Response ?? string.Empty));
            }
            catch (HttpRequestException) when (attempt < 2)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return ServiceResponse<ModelResult>.Fail(
            new DomainError(ErrorCodes.DependencyUnavailable, "Model inference failed."),
            statusCode: 503,
            message: "Model inference error");
    }

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaGenerateOptions Options { get; set; } = new();
    }

    private sealed class OllamaGenerateOptions
    {
        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }
}
#nullable disable
