namespace Cwi.Orchestrator.Application.BreakGlass;

public interface IBreakGlassNotifier
{
    Task NotifyAsync(BreakGlassNotification notification, CancellationToken cancellationToken);
}
