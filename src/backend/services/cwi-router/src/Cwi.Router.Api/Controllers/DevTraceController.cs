#nullable enable
using Cwi.Router.Api.Trace;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Router.Api.Controllers;

[ApiController]
[Route("api/dev/trace")]
public sealed class DevTraceController : ControllerBase
{
    private const string TokenHeader = "X-Trace-Query-Token";
    private readonly TraceSpanStore _store;
    private readonly TraceQueryOptions _options;

    public DevTraceController(TraceSpanStore store, TraceQueryOptions options)
    {
        _store = store;
        _options = options;
    }

    [HttpGet("{traceId}/spans")]
    public IActionResult GetSpans([FromRoute] string traceId)
    {
        if (!_options.Enabled)
        {
            return NotFound(new { error = "trace_query_disabled" });
        }

        if (!string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            var token = Request.Headers[TokenHeader].ToString();
            if (!string.Equals(token, _options.AuthToken, StringComparison.Ordinal))
            {
                return Unauthorized(new { error = "trace_query_unauthorized" });
            }
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
