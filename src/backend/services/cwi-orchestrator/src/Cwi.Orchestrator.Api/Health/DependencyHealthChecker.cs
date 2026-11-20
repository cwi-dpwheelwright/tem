using Cwi.Orchestrator.Api.Configuration;
using Npgsql;

namespace Cwi.Orchestrator.Api.Health;

public sealed class DependencyHealthChecker
{
    private readonly OrchestratorConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public DependencyHealthChecker(
        OrchestratorConfig config,
        IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DependencyHealthReport> CheckAsync(CancellationToken cancellationToken)
    {
        var databaseTask = CheckDatabaseAsync(cancellationToken);
        var provenanceTask = CheckProvenanceAsync(cancellationToken);
        var packsTask = CheckPacksAsync(cancellationToken);
        var vectorShard1Task = CheckVectorShardAsync("vector_shard_1", _config.VectorShard1Url, cancellationToken);
        var vectorShard2Task = CheckVectorShardAsync("vector_shard_2", _config.VectorShard2Url, cancellationToken);
        var modelTask = CheckModelAsync(cancellationToken);

        await Task.WhenAll(
            databaseTask,
            provenanceTask,
            packsTask,
            vectorShard1Task,
            vectorShard2Task,
            modelTask).ConfigureAwait(false);

        var database = await databaseTask.ConfigureAwait(false);
        var provenance = await provenanceTask.ConfigureAwait(false);
        var packs = await packsTask.ConfigureAwait(false);
        var vectorShard1 = await vectorShard1Task.ConfigureAwait(false);
        var vectorShard2 = await vectorShard2Task.ConfigureAwait(false);
        var model = await modelTask.ConfigureAwait(false);

        return new DependencyHealthReport(database, provenance, packs, vectorShard1, vectorShard2, model);
    }

    private async Task<DependencyStatus> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using var connection = new NpgsqlConnection(_config.DbConnectionString);
            await connection.OpenAsync(timeoutCts.Token).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(timeoutCts.Token).ConfigureAwait(false);
            return DependencyStatus.Ok("database");
        }
        catch (OperationCanceledException)
        {
            return DependencyStatus.Failed("database", "Timeout");
        }
        catch (Exception ex)
        {
            return DependencyStatus.Failed("database", ex.GetType().Name);
        }
    }

    private async Task<DependencyStatus> CheckProvenanceAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using var connection = new NpgsqlConnection(_config.GraphDbConnectionString);
            await connection.OpenAsync(timeoutCts.Token).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(timeoutCts.Token).ConfigureAwait(false);
            return DependencyStatus.Ok("provenance");
        }
        catch (OperationCanceledException)
        {
            return DependencyStatus.Failed("provenance", "Timeout");
        }
        catch (Exception ex)
        {
            return DependencyStatus.Failed("provenance", ex.GetType().Name);
        }
    }

    private Task<DependencyStatus> CheckPacksAsync(CancellationToken cancellationToken)
    {
        return CheckHttpAsync(
            "packs",
            _config.PacksBaseUrl,
            "/",
            status => status < 500,
            cancellationToken);
    }

    private Task<DependencyStatus> CheckVectorShardAsync(
        string name,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        return CheckHttpAsync(name, baseUrl, "/collections", status => status < 400, cancellationToken);
    }

    private Task<DependencyStatus> CheckModelAsync(CancellationToken cancellationToken)
    {
        return CheckHttpAsync("model", _config.ModelBaseUrl, "/api/tags", status => status < 400, cancellationToken);
    }

    private async Task<DependencyStatus> CheckHttpAsync(
        string name,
        string baseUrl,
        string path,
        Func<int, bool> statusPredicate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return DependencyStatus.Failed(name, "url_missing");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUri = new Uri(baseUrl.EndsWith("/") ? baseUrl : $"{baseUrl}/");
            var target = new Uri(baseUri, path.TrimStart('/'));
            using var response = await client.GetAsync(target, cancellationToken).ConfigureAwait(false);

            return statusPredicate((int)response.StatusCode)
                ? DependencyStatus.Ok(name)
                : DependencyStatus.Failed(name, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return DependencyStatus.Failed(name, ex.GetType().Name);
        }
    }
}
