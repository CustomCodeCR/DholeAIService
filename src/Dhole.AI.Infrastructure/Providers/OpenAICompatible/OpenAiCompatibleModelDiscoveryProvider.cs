using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.OpenAICompatible;

public sealed class OpenAiCompatibleModelDiscoveryProvider(IHttpClientFactory httpClientFactory)
    : IAiModelDiscoveryProvider
{
    public AiProviderType ProviderType => AiProviderType.OpenAICompatible;

    public async Task<IReadOnlyCollection<AiDiscoveredModel>> DiscoverAsync(
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        var url = ProviderUrl.Api(context.BaseUrl, "v1", "models");

        using var request = ProviderHttp.CreateJsonRequest(
            HttpMethod.Get,
            url,
            bearerToken: context.Secret
        );

        var client = httpClientFactory.CreateClient(AiHttpClientNames.OpenAICompatible);

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
            document.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idProperty))
                {
                    continue;
                }

                var id = idProperty.GetString();

                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                result.Add(
                    new AiDiscoveredModel(
                        id,
                        id,
                        AiModelCapabilityInference.FromModelName(id),
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
