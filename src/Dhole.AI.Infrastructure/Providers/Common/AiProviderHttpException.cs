using System.Net;

namespace Dhole.AI.Infrastructure.Providers.Common;

public sealed class AiProviderHttpException : Exception
{
    public AiProviderHttpException(HttpStatusCode statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }
}
