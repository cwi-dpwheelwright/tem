namespace Cwi.Coordination.Application.Models;

public sealed class NodeHelloValidationResult
{
    public bool IsValid { get; set; }

    public string? Reason { get; set; }
}
