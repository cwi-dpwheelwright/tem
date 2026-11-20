namespace Cwi.Coordination.Application;

public sealed class NodeSessionOptions
{
    public int KeepaliveIntervalSeconds { get; init; } = 20;
    public int HeartbeatIntervalSeconds { get; init; } = 45;
    public int TelemetryIntervalSeconds { get; init; } = 120;
    public int NodeReadyStaleAfterSeconds { get; init; } = 90;
    public int DispatchAckTimeoutSeconds { get; init; } = 5;
    public int DispatchExecutionTimeoutSeconds { get; init; } = 180;
    public int MaxPendingDispatchPerSession { get; init; } = 25;
}
