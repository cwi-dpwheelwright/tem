using Microsoft.AspNetCore.Mvc.Filters;

namespace Cwi.Orchestrator.Api.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class JwtRequiredAttribute : Attribute, IFilterMetadata
{
}
