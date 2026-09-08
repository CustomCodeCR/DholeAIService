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

    private const string CurrentBodyBegin = "[[CURRENT_EMAIL_BODY_BEGIN]]";
    private const string CurrentBodyEnd = "[[CURRENT_EMAIL_BODY_END]]";
    private const string AttachmentBegin = "[[ATTACHMENT_SOURCE_BEGIN]]";
    private const string AttachmentEnd = "[[ATTACHMENT_SOURCE_END]]";
    private const string BodySourceBegin = "[[CURRENT_EMAIL_SOURCE_BEGIN]]";
    private const string BodySourceEnd = "[[CURRENT_EMAIL_SOURCE_END]]";

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

        var result = await response.Content.ReadFromJsonAsync<
                DataExtractionAiEmailRequestResponse
            >(JsonOptions, timeout.Token)
            ?? throw new AiEmailJobException(
                "AI.DataExtractionPayloadInvalid",
                "DataExtraction devolvió un payload interno vacío o inválido.",
                isTransient: false
            );

        return HardenSourceIsolation(result);
    }

    private static DataExtractionAiEmailRequestResponse HardenSourceIsolation(
        DataExtractionAiEmailRequestResponse response
    )
    {
        if (response.Payload.ValueKind != JsonValueKind.Object)
        {
            return response;
        }

        AiPricingEmailPayload? payload;
        try
        {
            payload = response.Payload.Deserialize<AiPricingEmailPayload>(JsonOptions);
        }
        catch (JsonException)
        {
            return response;
        }

        if (payload is null)
        {
            return response;
        }

        var isBodySource = payload.SourceType.Contains(
            "Body",
            StringComparison.OrdinalIgnoreCase
        );

        var hardened = payload with
        {
            BodyText = HardenCurrentBody(payload.BodyText),
            BodyHtml = HardenCurrentBody(payload.BodyHtml),
            SourceContent = HardenSourceContent(payload, isBodySource),
        };

        return response with
        {
            Payload = JsonSerializer.SerializeToElement(hardened, JsonOptions),
        };
    }

    private static string? HardenCurrentBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Contains(CurrentBodyBegin, StringComparison.Ordinal))
        {
            return value;
        }

        return $"""
            {CurrentBodyBegin}
            REGLAS DE AISLAMIENTO DE FUENTE:
            - Esta sección representa únicamente el cuerpo del mensaje actual.
            - Firmas, cabeceras citadas y conversaciones reenviadas no reemplazan remitente, asunto ni datos tarifarios del mensaje actual.
            - No copies POL, POE, POD, naviera, equipo, fechas, moneda o montos desde un adjunto para completar esta sección.
            - Si un dato no aparece en esta fuente, déjalo nulo/desconocido en vez de tomarlo de otra fuente.

            {value}
            {CurrentBodyEnd}
            """;
    }

    private static string HardenSourceContent(
        AiPricingEmailPayload payload,
        bool isBodySource
    )
    {
        var value = payload.SourceContent ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (
            value.Contains(AttachmentBegin, StringComparison.Ordinal)
            || value.Contains(BodySourceBegin, StringComparison.Ordinal)
        )
        {
            return value;
        }

        if (isBodySource)
        {
            return $"""
                {BodySourceBegin}
                source_name: {payload.SourceName}
                source_type: {payload.SourceType}
                content_type: {payload.SourceContentType ?? "unknown"}
                REGLAS DE AISLAMIENTO DE FUENTE:
                - Extrae filas únicamente de este cuerpo de correo.
                - No completes campos usando información de adjuntos, firmas o historial citado.
                - Cada fila debe ser coherente dentro de esta misma fuente; si falta un campo, conserva null y agrega warning.

                {value}
                {BodySourceEnd}
                """;
        }

        return $"""
            {AttachmentBegin}
            source_name: {payload.SourceName}
            source_type: {payload.SourceType}
            content_type: {payload.SourceContentType ?? "unknown"}
            REGLAS DE AISLAMIENTO DE FUENTE:
            - Todo dato tarifario de esta sección pertenece únicamente a este adjunto.
            - No copies POL, POE, POD, naviera, equipo, fechas, moneda o montos desde el cuerpo del correo, historial citado ni otro adjunto.
            - El cuerpo del correo puede orientar qué adjunto corresponde a una región o vigencia, pero nunca puede aportar silenciosamente campos faltantes a una fila de este documento.
            - previousExtraction solo sirve para reparar la extracción de esta misma fuente; no autoriza mezclar documentos.
            - Si dos fuentes contradicen un campo, conserva la evidencia de esta fuente o devuelve null; nunca combines ambas para fabricar una fila completa.

            {value}
            {AttachmentEnd}
            """;
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
