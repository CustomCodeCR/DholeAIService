using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dhole.AI.Worker.Health;

internal sealed class DataExtractionInternalEndpointHealthCheck(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration
) : IHealthCheck
{
    internal const string HttpClientName =
        "data-extraction-internal-health";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var configuredBaseUrl =
            configuration["DataExtraction:InternalBaseUrl"];
        if (
            string.IsNullOrWhiteSpace(configuredBaseUrl)
            || !Uri.TryCreate(
                configuredBaseUrl.TrimEnd('/') + "/",
                UriKind.Absolute,
                out var baseUri
            )
        )
        {
            return HealthCheckResult.Degraded(
                "DataExtraction:InternalBaseUrl no está configurado."
            );
        }

        var endpoint = new Uri(
            baseUri,
            "api/internal/data-extraction/ai-email-requests/"
                + Guid.Empty
        );
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

        try
        {
            using var response = await httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );
            var data = new Dictionary<string, object>
            {
                ["status_code"] = (int)response.StatusCode,
                ["endpoint_host"] = endpoint.Host,
            };

            return response.IsSuccessStatusCode
                || response.StatusCode == HttpStatusCode.NotFound
                || response.StatusCode == HttpStatusCode.Conflict
                ? HealthCheckResult.Healthy(
                    "El endpoint interno de DataExtraction es accesible.",
                    data
                )
                : HealthCheckResult.Degraded(
                    "El endpoint interno de DataExtraction respondió con error.",
                    data: data
                );
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                or TaskCanceledException)
        {
            return HealthCheckResult.Degraded(
                "No se pudo alcanzar el endpoint interno de DataExtraction.",
                exception
            );
        }
    }
}
