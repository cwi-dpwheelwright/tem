namespace Cwi.Common.Errors;

public sealed class DomainError
{
    public string Code { get; }
    public string Message { get; }
    public string Error => Code;

    public DomainError(string code, string message)
    {
        Code = code;
        Message = message;
    }
}
