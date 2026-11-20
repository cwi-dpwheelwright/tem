using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Cwi.Orchestrator.Api.BreakGlass;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class BreakGlassScopeAttribute : Attribute, IFilterMetadata
{
    public BreakGlassScopeAttribute(string scope)
    {
        Scope = scope;
    }

    public string Scope { get; }
}
