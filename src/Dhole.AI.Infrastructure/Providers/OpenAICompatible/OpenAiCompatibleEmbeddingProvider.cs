using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.OpenAICompatible;

public sealed class OpenAiCompatibleEmbeddingProvider(IHttpClientFactory httpClientFactory)
    : IAiEmbeddingProvider
{
    public AiProviderType ProviderType => AiProviderType.OpenAICompatible;

    public async Task<AiProviderEmbeddingResponse> ExecuteAsync(
        AiProviderEmbeddingRequest request,
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        var model = ProviderGuards.RequireExternalModelId(context);

        var url = ProviderUrl.Api(context.BaseUrl, "v1", "embeddings");

        var payload = OpenAiCompatibleMapper.CreateEmbeddingPayload(request, model);

        using var httpRequest = ProviderHttp.CreateJsonRequest(
            HttpMethod.Post,
            url,
            payload,
            context.Secret
        );

        var client = httpClientFactory.CreateClient(AiHttpClientNames.OpenAICompatible);

        using var timeout = ProviderHttp.CreateTimeout(context.TimeoutSeconds, cancellationToken);

        using var response = await client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );

        await ProviderHttp.EnsureSuccessAsync(response, timeout.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: timeout.Token
        );

        var root = document.RootElement;
        var embeddings = new List<IReadOnlyCollection<float>>();

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (
                    !item.TryGetProperty("embedding", out var vector)
                    || vector.ValueKind != JsonValueKind.Array
                )
                {
                    continue;
                }

                embeddings.Add(
                    vector.EnumerateArray().Select(value => value.GetSingle()).ToArray()
                );
            }
        }

        var inputTokens = 0;

        if (root.TryGetProperty("usage", out var usage))
        {
            inputTokens = ProviderJson.GetInt32(usage, "prompt_tokens");
        }

        return new AiProviderEmbeddingResponse(
            embeddings,
            embeddings.FirstOrDefault()?.Count ?? 0,
            inputTokens,
            root.GetRawText()
        );
    }
}
