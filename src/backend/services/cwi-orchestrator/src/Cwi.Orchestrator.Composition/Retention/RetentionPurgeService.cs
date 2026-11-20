using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using Cwi.Orchestrator.Application.Retention;
using Cwi.Orchestrator.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Composition.Retention;

public sealed class RetentionPurgeService
{
    private const string LedgerEntityName = "LedgerEvent";
    private const string TelemetryEntityName = "TelemetryWindow";
    private const string CreatedUtcProperty = "CreatedUtc";

    private readonly OrchestratorDbContext _dbContext;
    private readonly RetentionPolicyOptions _options;
    private readonly ILogger<RetentionPurgeService> _logger;

    public RetentionPurgeService(
        OrchestratorDbContext dbContext,
        RetentionPolicyOptions options,
        ILogger<RetentionPurgeService> logger)
    {
        _dbContext = dbContext;
        _options = options;
        _logger = logger;
    }

    public async Task<RetentionPurgeResult> PurgeAsync(CancellationToken cancellationToken)
    {
        if (!_options.PurgeEnabled)
        {
            return new RetentionPurgeResult();
        }

        var now = DateTimeOffset.UtcNow;
        var ledgerRemoved = 0;
        var telemetryRemoved = 0;

        if (_options.LedgerDays > 0)
        {
            var cutoff = now.AddDays(-_options.LedgerDays);
            ledgerRemoved = await MarkExpiredAsync(LedgerEntityName, cutoff, cancellationToken).ConfigureAwait(false);
        }

        if (_options.TelemetryDays > 0)
        {
            var cutoff = now.AddDays(-_options.TelemetryDays);
            telemetryRemoved = await MarkExpiredAsync(TelemetryEntityName, cutoff, cancellationToken).ConfigureAwait(false);
        }

        if (ledgerRemoved > 0 || telemetryRemoved > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Retention purge completed. Ledger removed: {LedgerCount}. Telemetry removed: {TelemetryCount}.",
                ledgerRemoved,
                telemetryRemoved);
        }

        return new RetentionPurgeResult
        {
            LedgerRemoved = ledgerRemoved,
            TelemetryRemoved = telemetryRemoved
        };
    }

    private async Task<int> MarkExpiredAsync(
        string entityName,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var entityType = _dbContext.Model.GetEntityTypes()
            .FirstOrDefault(type => string.Equals(type.ClrType.Name, entityName, StringComparison.Ordinal));
        if (entityType is null)
        {
            _logger.LogWarning("Retention purge skipped; entity type not found: {EntityName}", entityName);
            return 0;
        }

        var expired = await LoadExpiredAsync(entityType.ClrType, cutoff, cancellationToken).ConfigureAwait(false);
        var count = expired.Count;
        if (count > 0)
        {
            _dbContext.RemoveRange(expired.Cast<object>());
        }

        return count;
    }

    private async Task<IList> LoadExpiredAsync(
        Type entityType,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var setMethod = typeof(DbContext)
            .GetMethods()
            .Single(method => method.Name == nameof(DbContext.Set)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
        var set = (IQueryable)setMethod.MakeGenericMethod(entityType)
            .Invoke(_dbContext, null)!;
        var parameter = Expression.Parameter(entityType, "e");
        var createdUtc = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            new[] { typeof(DateTimeOffset) },
            parameter,
            Expression.Constant(CreatedUtcProperty));
        var cutoffConstant = Expression.Constant(cutoff);
        var predicate = Expression.Lambda(Expression.LessThan(createdUtc, cutoffConstant), parameter);

        var whereCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            new[] { entityType },
            set.Expression,
            predicate);

        var query = set.Provider.CreateQuery(whereCall);
        var toListAsync = typeof(EntityFrameworkQueryableExtensions)
            .GetMethods()
            .Single(method => method.Name == nameof(EntityFrameworkQueryableExtensions.ToListAsync)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 2)
            .MakeGenericMethod(entityType);

        var task = (Task)toListAsync.Invoke(null, new object?[] { query, cancellationToken })!;
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        return (IList)(resultProperty?.GetValue(task) ?? Array.Empty<object>());
    }
}

public sealed class RetentionPurgeResult
{
    public int LedgerRemoved { get; init; }
    public int TelemetryRemoved { get; init; }
}
