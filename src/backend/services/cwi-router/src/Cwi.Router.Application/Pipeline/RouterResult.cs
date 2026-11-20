namespace Cwi.Router.Application.Pipeline;

public sealed class RouterResult
{
    public int StatusCode { get; }
    public string? Body { get; }
    public string ContentType { get; }
    public Stream? BodyStream { get; }
    public bool IsStream => BodyStream is not null;

    public RouterResult(int statusCode, string body, string contentType)
    {
        StatusCode = statusCode;
        Body = body;
        ContentType = contentType;
    }

    public RouterResult(int statusCode, Stream bodyStream, string contentType)
    {
        StatusCode = statusCode;
        Body = null;
        BodyStream = bodyStream;
        ContentType = contentType;
    }
}
