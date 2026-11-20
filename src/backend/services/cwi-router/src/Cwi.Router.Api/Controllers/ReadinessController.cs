using System.Net.Http.Json;
using Cwi.Router.Api.Models;
using Cwi.Router.Application.Health;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Router.Api.Controllers;

[ApiController]
public sealed class ReadinessController : ControllerBase
{
    private readonly DependencyHealthChecker _checker;
    private readonly Cwi.Router.Application.Survival.SurvivalStateStore _survivalStore;
    private readonly Cwi.Router.Application.Survival.SurvivalOptions _survivalOptions;
    private readonly IHttpClientFactory _httpClientFactory;

    public ReadinessController(
        DependencyHealthChecker checker,
        Cwi.Router.Application.Survival.SurvivalStateStore survivalStore,
        Cwi.Router.Application.Survival.SurvivalOptions survivalOptions,
        IHttpClientFactory httpClientFactory)
    {
        _checker = checker;
        _survivalStore = survivalStore;
        _survivalOptions = survivalOptions;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("api/ready")]
    public async Task<ActionResult<ReadyResponse>> Ready(CancellationToken cancellationToken)
    {
        var report = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        var checks = new Dictionary<string, ReadyCheck>
        {
            [report.Orchestrator.Name] = BuildCheck(report.Orchestrator),
            [report.Redis.Name] = BuildCheck(report.Redis)
        };

        var survival = _survivalStore.GetSnapshot();
        checks["survival"] = new ReadyCheck
        {
            Status = string.Equals(survival.Mode, "normal", StringComparison.OrdinalIgnoreCase) ? "ok" : "degraded",
            Detail = string.Equals(survival.Mode, "normal", StringComparison.OrdinalIgnoreCase)
                ? null
                : $"mode={survival.Mode}"
        };

        var ready = report.IsHealthy;
        var orchestratorReady = await TryGetOrchestratorReadyAsync(cancellationToken).ConfigureAwait(false);
        if (orchestratorReady is not null)
        {
            foreach (var entry in orchestratorReady.Checks)
            {
                checks[entry.Key] = new ReadyCheck
                {
                    Status = entry.Value.Status,
                    Detail = entry.Value.Detail
                };
            }

            if (!orchestratorReady.Ready)
            {
                ready = false;
            }
        }
        if (_survivalOptions.ReadinessFailsOnSurvival &&
            (string.Equals(survival.Mode, "survival", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(survival.Mode, "degraded", StringComparison.OrdinalIgnoreCase)))
        {
            ready = false;
        }

        var response = new ReadyResponse
        {
            Ready = ready,
            Checks = checks,
            Timestamp = DateTimeOffset.UtcNow
        };

        return ready ? Ok(response) : StatusCode(503, response);
    }

    private static ReadyCheck BuildCheck(DependencyStatus status)
    {
        return new ReadyCheck
        {
            Status = status.IsHealthy ? "ok" : "down",
            Detail = status.IsHealthy ? null : status.Detail
        };
    }

    private async Task<OrchestratorReadyResponse?> TryGetOrchestratorReadyAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            var client = _httpClientFactory.CreateClient("orchestrator");
            using var response = await client.GetAsync("/api/ready", timeoutCts.Token).ConfigureAwait(false);
            var payload = await response.Content.ReadFromJsonAsync<OrchestratorReadyResponse>(
                cancellationToken: timeoutCts.Token).ConfigureAwait(false);
            return payload;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record OrchestratorReadyResponse(
        bool Ready,
        Dictionary<string, OrchestratorReadyCheck> Checks);

    private sealed record OrchestratorReadyCheck(
        string Status,
        string? Detail);
}
