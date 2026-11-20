#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Runtime;

public interface IRuntimeCriticClient
{
    Task<RuntimeCriticResult?> EvaluateAsync(
        AskWorkflowContext context,
        AskResponseDto response,
        CancellationToken cancellationToken);
}
#nullable disable
