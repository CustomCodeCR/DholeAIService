using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dhole.AI.Worker.EmailAnalysis;

internal interface IDataExtractionAiEmailRequestClient
{
    Task<DataExtractionAiEmailRequestResponse> GetAsync(
        string payloadUrl,
        CancellationToken cancellationToken
    );
}

internal sealed class DataExtractionAiEmailRequestClient(
    HttpClient httpClient,
    IConfiguration configuration
) : IDataExtractionAiEmailRequestClient
{
    private const string CanonicalRequestPath =
        "/api/internal/data-extraction/ai-email-requests/";

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<DataExtractionAiEmailRequestResponse> GetAsync(
        string payloadUrl,
        CancellationToken cancellationToken
    )
    {
        var endpoint = ResolveEndpoint(payloadUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var timeout = CreateTimeout(cancellationToken);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );

        if (!response.IsSuccessStatusCode)
        {
            throw CreateHttpException(response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<
                DataExtractionAiEmailRequestResponse
            >(JsonOptions, timeout.Token)
            ?? throw new AiEmailJobException(
                "AI.DataExtractionPayloadInvalid",
                "DataExtraction devolvió un payload interno vacío o inválido.",
                isTransient: false
            );
    }

    private Uri ResolveEndpoint(string value)
    {
        var configuredBaseUrl = configuration["DataExtraction:InternalBaseUrl"];
        if (
            string.IsNullOrWhiteSpace(configuredBaseUrl)
            || !Uri.TryCreate(
                configuredBaseUrl.TrimEnd('/') + "/",
                UriKind.Absolute,
                out var baseUri
            )
        )
        {
            throw new AiEmailJobException(
                "AI.DataExtractionBaseUrlMissing",
                "No se configuró DataExtraction:InternalBaseUrl.",
                isTransient: false
            );
        }

        var canonicalPath = ResolveCanonicalPath(value);
        return new Uri(baseUri, canonicalPath.TrimStart('/'));
    }

    private static string ResolveCanonicalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw RejectedPayloadUrl();
        }

        string path;
        string query;
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var absolute))
        {
            path = absolute.AbsolutePath;
            query = absolute.Query;
        }
        else
        {
            var raw = value.Trim();
            var queryIndex = raw.IndexOf('?');
            path = queryIndex >= 0 ? raw[..queryIndex] : raw;
            query = queryIndex >= 0 ? raw[queryIndex..] : string.Empty;
        }

        var canonicalIndex = path.IndexOf(
            CanonicalRequestPath,
            StringComparison.OrdinalIgnoreCase
        );
        if (canonicalIndex < 0)
        {
            throw RejectedPayloadUrl();
        }

        var canonical = path[canonicalIndex..];
        var identifier = canonical[CanonicalRequestPath.Length..]
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (!Guid.TryParse(identifier, out _))
        {
            throw RejectedPayloadUrl();
        }

        return canonical + query;
    }

    private static AiEmailJobException RejectedPayloadUrl()
    {
        return new AiEmailJobException(
            "AI.DataExtractionPayloadUrlRejected",
            "La ruta del payload de DataExtraction no es válida.",
            isTransient: false
        );
    }

    private CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken
    )
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeout.CancelAfter(
            TimeSpan.FromSeconds(
                ReadPositiveInt(
                    configuration["DataExtraction:TimeoutSeconds"],
                    60
                )
            )
        );
        return timeout;
    }

    private static AiEmailJobException CreateHttpException(
        HttpStatusCode statusCode
    )
    {
        var isTransient =
            statusCode
            is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;
        return new AiEmailJobException(
            $"AI.DataExtractionHttp{(int)statusCode}",
            $"DataExtraction respondió HTTP {(int)statusCode}.",
            isTransient
        );
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}

internal sealed class AiEmailJobException(
    string errorCode,
    string message,
    bool isTransient,
    Exception? innerException = null
) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;

    public bool IsTransient { get; } = isTransient;
}
