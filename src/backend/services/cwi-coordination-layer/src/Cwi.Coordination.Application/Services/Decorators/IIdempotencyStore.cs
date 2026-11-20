namespace Cwi.Coordination.Application.Services.Decorators;

public interface IIdempotencyStore
{
    bool TryBegin(string key);
    void Complete(string key);
}
