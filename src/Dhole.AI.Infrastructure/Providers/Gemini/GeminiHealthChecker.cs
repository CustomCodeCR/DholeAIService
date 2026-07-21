using System.Diagnostics;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Gemini;

public sealed class GeminiHealthChecker(IHttpClientFactory httpClientFactory)
    : IAiProviderHealthChecker
{
    public AiProviderType ProviderType => AiProviderType.Gemini;

    public async Task<AiProviderHealthResult> CheckAsync(
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(context.Secret))
            {
                throw new InvalidOperationException("La API key de Gemini es obligatoria.");
            }

            var url = ProviderUrl.Api(context.BaseUrl, "v1beta", "models");

            using var request = ProviderHttp.CreateJsonRequest(
                HttpMethod.Get,
                url,
                headers: new Dictionary<string, string> { ["x-goog-api-key"] = context.Secret }
            );

            var client = httpClientFactory.CreateClient(AiHttpClientNames.Gemini);

            using var timeout = ProviderHttp.CreateTimeout(
                context.TimeoutSeconds,
                cancellationToken
            );

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );

            await ProviderHttp.EnsureSuccessAsync(response, timeout.Token);

            stopwatch.Stop();

            return new AiProviderHealthResult(true, stopwatch.ElapsedMilliseconds, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();

            return new AiProviderHealthResult(
                false,
                stopwatch.ElapsedMilliseconds,
                DateTime.UtcNow,
                "AI.Gemini.HealthCheckFailed",
                exception.Message
            );
        }
    }
}
