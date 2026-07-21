using System.Diagnostics;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.OpenAI;

public sealed class OpenAiHealthChecker(IHttpClientFactory httpClientFactory)
    : IAiProviderHealthChecker
{
    public AiProviderType ProviderType => AiProviderType.OpenAI;

    public async Task<AiProviderHealthResult> CheckAsync(
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var url = ProviderUrl.Api(context.BaseUrl, "v1", "models");

            using var request = ProviderHttp.CreateJsonRequest(
                HttpMethod.Get,
                url,
                bearerToken: context.Secret
            );

            var client = httpClientFactory.CreateClient(AiHttpClientNames.OpenAI);

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
                "AI.OpenAI.HealthCheckFailed",
                exception.Message
            );
        }
    }
}
