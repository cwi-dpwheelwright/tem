#nullable enable
using Cwi.Orchestrator.Application.Trace;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/trace")]
public sealed class TraceController : ControllerBase
{
    private readonly TraceLookupStore _traceStore;

    public TraceController(TraceLookupStore traceStore)
    {
        _traceStore = traceStore;
    }

    [HttpGet("{traceId}")]
    public IActionResult Get(string traceId)
    {
        var docId = _traceStore.TryGetDocId(traceId);
        if (string.IsNullOrWhiteSpace(docId))
        {
            return NotFound(new { trace_id = traceId });
        }

        return Ok(new { trace_id = traceId, doc_id = docId });
    }
}
#nullable disable
