#nullable enable
using System.Net.Http.Json;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Mcp;

public interface IMcpRegistryClient
{
    Task<ServiceResponse<McpToolRegistryDocument>> GetRegistryAsync(CancellationToken cancellationToken);
}

public sealed class HttpMcpRegistryClient : IMcpRegistryClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly McpExecutionOptions _options;

    public HttpMcpRegistryClient(IHttpClientFactory httpClientFactory, McpExecutionOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<ServiceResponse<McpToolRegistryDocument>> GetRegistryAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ToolRegistryUrl))
        {
            return ServiceResponse<McpToolRegistryDocument>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "MCP tool registry URL is not configured."),
                statusCode: 503,
                message: "MCP tool registry unavailable");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("mcp");
            var registry = await client.GetFromJsonAsync<McpToolRegistryDocument>(
                    _options.ToolRegistryUrl,
                    cancellationToken)
                .ConfigureAwait(false);

            if (registry is null)
            {
                return ServiceResponse<McpToolRegistryDocument>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "MCP tool registry returned an empty document."),
                    statusCode: 503,
                    message: "MCP tool registry unavailable");
            }

            return ServiceResponse<McpToolRegistryDocument>.Success(registry);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException)
        {
            return ServiceResponse<McpToolRegistryDocument>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "MCP tool registry could not be read."),
                statusCode: 503,
                message: "MCP tool registry unavailable");
        }
    }
}
#nullable disable
