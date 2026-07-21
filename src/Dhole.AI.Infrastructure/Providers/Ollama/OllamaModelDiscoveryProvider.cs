using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Ollama;

public sealed class OllamaModelDiscoveryProvider(IHttpClientFactory httpClientFactory)
    : IAiModelDiscoveryProvider
{
    public AiProviderType ProviderType => AiProviderType.Ollama;

    public async Task<IReadOnlyCollection<AiDiscoveredModel>> DiscoverAsync(
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        var url = ProviderUrl.Combine(context.BaseUrl, "api/tags");

        using var request = ProviderHttp.CreateJsonRequest(
            HttpMethod.Get,
            url,
            bearerToken: context.Secret
        );

        var client = httpClientFactory.CreateClient(AiHttpClientNames.Ollama);

        using var timeout = ProviderHttp.CreateTimeout(context.TimeoutSeconds, cancellationToken);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );

        await ProviderHttp.EnsureSuccessAsync(response, timeout.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: timeout.Token
        );

        var result = new List<AiDiscoveredModel>();

        if (
            document.RootElement.TryGetProperty("models", out var models)
            && models.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var model in models.EnumerateArray())
            {
                var name = model.TryGetProperty("name", out var nameProperty)
                    ? nameProperty.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Add(
                    new AiDiscoveredModel(
                        name,
                        name,
                        AiModelCapabilityInference.FromModelName(name),
                        null,
                        null,
                        true
                    )
                );
            }
        }

        return result;
    }
}
