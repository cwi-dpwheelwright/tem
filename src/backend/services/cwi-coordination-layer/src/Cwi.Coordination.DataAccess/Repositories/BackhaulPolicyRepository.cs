using System.Text.Json;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class BackhaulPolicyRepository : IBackhaulPolicyRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CoordinationDbContext _db;

    public BackhaulPolicyRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task<BackhaulPolicy> GetAsync(CancellationToken cancellationToken)
    {
        var record = await _db.BackhaulPolicies
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            var policy = BuildDefaultPolicy();
            record = new BackhaulPolicyRecord
            {
                Id = Guid.NewGuid(),
                RequireAuthenticatedChannel = policy.RequireAuthenticatedChannel,
                AllowedInterfacesJson = SerializeInterfaces(policy.AllowedInterfaces),
                DeniedInterfacesJson = SerializeInterfaces(policy.DeniedInterfaces),
                RevokedPeersJson = SerializeStrings(policy.RevokedPeers),
                PolicyVersion = policy.PolicyVersion,
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            _db.BackhaulPolicies.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Map(record);
    }

    public async Task<BackhaulPolicy> SetAsync(BackhaulPolicy policy, CancellationToken cancellationToken)
    {
        var record = await _db.BackhaulPolicies
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            record = new BackhaulPolicyRecord
            {
                Id = Guid.NewGuid()
            };
            _db.BackhaulPolicies.Add(record);
        }

        record.RequireAuthenticatedChannel = policy.RequireAuthenticatedChannel;
        record.AllowedInterfacesJson = SerializeInterfaces(policy.AllowedInterfaces);
        record.DeniedInterfacesJson = SerializeInterfaces(policy.DeniedInterfaces);
        record.RevokedPeersJson = SerializeStrings(policy.RevokedPeers);
        record.PolicyVersion = string.IsNullOrWhiteSpace(policy.PolicyVersion) ? "1.0" : policy.PolicyVersion;
        record.UpdatedUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(record);
    }

    private static BackhaulPolicy Map(BackhaulPolicyRecord record)
    {
        return new BackhaulPolicy
        {
            RequireAuthenticatedChannel = record.RequireAuthenticatedChannel,
            AllowedInterfaces = DeserializeInterfaces(record.AllowedInterfacesJson),
            DeniedInterfaces = DeserializeInterfaces(record.DeniedInterfacesJson),
            RevokedPeers = DeserializeStrings(record.RevokedPeersJson),
            PolicyVersion = record.PolicyVersion
        };
    }

    private static BackhaulPolicy BuildDefaultPolicy()
    {
        return new BackhaulPolicy
        {
            RequireAuthenticatedChannel = true,
            AllowedInterfaces = Array.Empty<InterfaceKind>(),
            DeniedInterfaces = Array.Empty<InterfaceKind>(),
            RevokedPeers = Array.Empty<string>(),
            PolicyVersion = "1.0"
        };
    }

    private static string SerializeInterfaces(IReadOnlyList<InterfaceKind> values)
    {
        var items = values.Select(v => v.ToString().ToLowerInvariant()).ToList();
        return JsonSerializer.Serialize(items, JsonOptions);
    }

    private static IReadOnlyList<InterfaceKind> DeserializeInterfaces(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<InterfaceKind>();
        }

        var items = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        return items.Select(ParseInterface).ToList();
    }

    private static string SerializeStrings(IReadOnlyList<string> values)
    {
        return JsonSerializer.Serialize(values ?? Array.Empty<string>(), JsonOptions);
    }

    private static IReadOnlyList<string> DeserializeStrings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
    }

    private static InterfaceKind ParseInterface(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "wifi" => InterfaceKind.Wifi,
            "cellular" => InterfaceKind.Cellular,
            "ethernet" => InterfaceKind.Ethernet,
            "satellite" => InterfaceKind.Satellite,
            "lora" => InterfaceKind.LoRa,
            _ => InterfaceKind.Unknown
        };
    }
}
