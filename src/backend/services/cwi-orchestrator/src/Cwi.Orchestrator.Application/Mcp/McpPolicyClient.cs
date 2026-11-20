#nullable enable
using System.Net.Http.Json;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Mcp;

public interface IMcpPolicyClient
{
    Task<ServiceResponse<McpPolicyDocument>> GetPolicyAsync(CancellationToken cancellationToken);
}

public sealed class HttpMcpPolicyClient : IMcpPolicyClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly McpExecutionOptions _options;

    public HttpMcpPolicyClient(IHttpClientFactory httpClientFactory, McpExecutionOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<ServiceResponse<McpPolicyDocument>> GetPolicyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.PolicyRulesUrl))
        {
            return ServiceResponse<McpPolicyDocument>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "MCP policy URL is not configured."),
                statusCode: 503,
                message: "MCP policy unavailable");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("mcp");
            var policy = await client.GetFromJsonAsync<McpPolicyDocument>(
                    _options.PolicyRulesUrl,
                    cancellationToken)
                .ConfigureAwait(false);

            if (policy is null)
            {
                return ServiceResponse<McpPolicyDocument>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "MCP policy returned an empty document."),
                    statusCode: 503,
                    message: "MCP policy unavailable");
            }

            return ServiceResponse<McpPolicyDocument>.Success(policy);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException)
        {
            return ServiceResponse<McpPolicyDocument>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "MCP policy could not be read."),
                statusCode: 503,
                message: "MCP policy unavailable");
        }
    }
}
#nullable disable
