#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.HomeNodes;

public sealed class HomeNodePolicyScopeService : DomainServiceBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHomeNodePolicyScopeRepository _repository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public HomeNodePolicyScopeService(
        IHomeNodePolicyScopeRepository repository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _repository = repository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task<ServiceResponse<HomeNodePolicyScopeDto>> UpsertAsync(
        HomeNodePolicyScopeUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleUpsertAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<IReadOnlyList<HomeNodePolicyScopeDto>>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleListAsync(tenantId, cancellationToken));
    }

    public Task<ServiceResponse<HomeNodePolicyScopeDto>> GetAsync(
        string tenantId,
        string policyScopeId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleGetAsync(tenantId, policyScopeId, cancellationToken));
    }

    private async Task<ServiceResponse<HomeNodePolicyScopeDto>> HandleUpsertAsync(
        HomeNodePolicyScopeUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Fail<HomeNodePolicyScopeDto>("tenant_id is required.", "tenant_id required");
        }

        if (string.IsNullOrWhiteSpace(request.PolicyScopeId))
        {
            return Fail<HomeNodePolicyScopeDto>("policy_scope_id is required.", "policy_scope_id required");
        }

        var tenantId = request.TenantId.Trim();
        var policyScopeId = request.PolicyScopeId.Trim();
        var now = DateTimeOffset.UtcNow;
        var existing = await _repository.GetAsync(tenantId, policyScopeId, cancellationToken).ConfigureAwait(false);

        var scope = existing ?? new HomeNodePolicyScope
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PolicyScopeId = policyScopeId,
            CreatedUtc = now
        };

        scope.RulesJson = JsonSerializer.Serialize(request.Rules ?? new Dictionary<string, object?>(), SerializerOptions);
        scope.Version = string.IsNullOrWhiteSpace(request.Version)
            ? existing?.Version ?? "1.0"
            : request.Version.Trim();
        scope.UpdatedUtc = now;

        await _repository.UpsertAsync(scope, cancellationToken).ConfigureAwait(false);
        await AppendLedgerEventAsync(scope, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<HomeNodePolicyScopeDto>.Success(Map(scope));
    }

    private async Task<ServiceResponse<IReadOnlyList<HomeNodePolicyScopeDto>>> HandleListAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail<IReadOnlyList<HomeNodePolicyScopeDto>>("tenant_id is required.", "tenant_id required");
        }

        var scopes = await _repository.ListAsync(tenantId.Trim(), cancellationToken).ConfigureAwait(false);
        return ServiceResponse<IReadOnlyList<HomeNodePolicyScopeDto>>.Success(scopes.Select(Map).ToList());
    }

    private async Task<ServiceResponse<HomeNodePolicyScopeDto>> HandleGetAsync(
        string tenantId,
        string policyScopeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail<HomeNodePolicyScopeDto>("tenant_id is required.", "tenant_id required");
        }

        if (string.IsNullOrWhiteSpace(policyScopeId))
        {
            return Fail<HomeNodePolicyScopeDto>("policy_scope_id is required.", "policy_scope_id required");
        }

        var scope = await _repository.GetAsync(tenantId.Trim(), policyScopeId.Trim(), cancellationToken).ConfigureAwait(false);
        if (scope is null)
        {
            return ServiceResponse<HomeNodePolicyScopeDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "home node policy scope not found."),
                statusCode: 404,
                message: "home node policy scope not found");
        }

        return ServiceResponse<HomeNodePolicyScopeDto>.Success(Map(scope));
    }

    private async Task AppendLedgerEventAsync(HomeNodePolicyScope scope, CancellationToken cancellationToken)
    {
        var traceId = $"home_node_policy_scope:{scope.TenantId}:{scope.PolicyScopeId}";
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new
        {
            tenant_id = scope.TenantId,
            policy_scope_id = scope.PolicyScopeId,
            version = scope.Version,
            updated_utc = scope.UpdatedUtc
        }, SerializerOptions);

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "home_node.policy_scope_upserted",
            payload,
            prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private static HomeNodePolicyScopeDto Map(HomeNodePolicyScope scope)
    {
        return new HomeNodePolicyScopeDto
        {
            TenantId = scope.TenantId,
            PolicyScopeId = scope.PolicyScopeId,
            Rules = JsonSerializer.Deserialize<Dictionary<string, object?>>(scope.RulesJson, SerializerOptions)
                ?? new Dictionary<string, object?>(),
            Version = scope.Version,
            CreatedUtc = scope.CreatedUtc,
            UpdatedUtc = scope.UpdatedUtc
        };
    }

    private static ServiceResponse<T> Fail<T>(string errorMessage, string message)
    {
        return ServiceResponse<T>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, errorMessage),
            statusCode: 400,
            message: message);
    }
}
#nullable disable
