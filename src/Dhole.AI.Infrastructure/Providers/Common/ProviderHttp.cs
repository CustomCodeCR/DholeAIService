using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dhole.AI.Infrastructure.Providers.Common;

internal static class ProviderHttp
{
    public static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string url,
        JsonNode? payload = null,
        string? bearerToken = null,
        IReadOnlyDictionary<string, string>? headers = null
    )
    {
        var request = new HttpRequestMessage(method, url);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (payload is not null)
        {
            request.Content = new StringContent(
                payload.ToJsonString(ProviderJson.Options),
                Encoding.UTF8,
                "application/json"
            );
        }

        return request;
    }

    public static CancellationTokenSource CreateTimeout(
        int timeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        return timeout;
    }

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        var providerMessage = ReadProviderError(body);
        var message = $"El proveedor respondió con el estado HTTP "
            + $"{(int)response.StatusCode} "
            + $"({response.ReasonPhrase}).";

        if (!string.IsNullOrWhiteSpace(providerMessage))
        {
            message += $" Detalle del proveedor: {providerMessage}";
        }

        throw new AiProviderHttpException(
            response.StatusCode,
            body,
            message
        );
    }
    private static string? ReadProviderError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
            )
            {
                return Limit(error.GetString(), 1_000);
            }
        }
        catch (JsonException)
        {
            // Algunos proxies devuelven texto plano o HTML. Se conserva un extracto.
        }

        return Limit(body, 1_000);
    }

    private static string? Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

}
