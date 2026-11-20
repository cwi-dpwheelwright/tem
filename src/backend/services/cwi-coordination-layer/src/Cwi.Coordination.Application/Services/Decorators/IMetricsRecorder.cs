namespace Cwi.Coordination.Application.Services.Decorators;

public interface IMetricsRecorder
{
    void Increment(string name);
}
