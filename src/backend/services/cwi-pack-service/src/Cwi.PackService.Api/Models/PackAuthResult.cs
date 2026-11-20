namespace Cwi.PackService.Api.Models;

public sealed record PackAuthResult(string? ActorId, string? TenantId, bool HasBreakGlass);
