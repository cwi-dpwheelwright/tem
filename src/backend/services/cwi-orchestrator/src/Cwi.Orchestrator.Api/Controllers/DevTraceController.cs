#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Api.Trace;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/dev/trace")]
public sealed class DevTraceController : ControllerBase
{
    private readonly TraceSpanStore _store;
    private readonly TraceQueryOptions _options;

    public DevTraceController(TraceSpanStore store, TraceQueryOptions options)
    {
        _store = store;
        _options = options;
    }

    [HttpGet("{traceId}/spans")]
    [BreakGlassScope("admin")]
    public IActionResult GetSpans([FromRoute] string traceId)
    {
        if (!_options.Enabled)
        {
            return NotFound(new { error = "trace_query_disabled" });
        }

        var spans = _store.GetSpans(traceId);
        if (spans.Count == 0)
        {
            return NotFound(new { trace_id = traceId, spans = Array.Empty<object>() });
        }

        return Ok(new { trace_id = traceId, spans });
    }
}
#nullable disable
