using System.Text;
using System.Text.Json;
using Cwi.PackService.Api.Configuration;
using Cwi.PackService.Api.Models;

namespace Cwi.PackService.Api.Infrastructure;

public sealed class PackAuditService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AuditConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PackAuditService> _logger;

    public PackAuditService(AuditConfig config, IHttpClientFactory httpClientFactory, ILogger<PackAuditService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void QueueAudit(PackAuditEvent auditEvent, string? actorId, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(_config.AuditUrl))
        {
            return;
        }

        auditEvent.ActorId ??= actorId;
        auditEvent.TenantId ??= tenantId;
        auditEvent.OccurredUtc = DateTimeOffset.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);

                var payload = JsonSerializer.Serialize(auditEvent, AuditJsonOptions);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(_config.AuditUrl, content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Audit dispatch failed with status {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit dispatch failed.");
            }
        });
    }
}
