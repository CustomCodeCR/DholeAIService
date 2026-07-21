using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Ollama;

public sealed class OllamaEmbeddingProvider(IHttpClientFactory httpClientFactory)
    : IAiEmbeddingProvider
{
    public AiProviderType ProviderType => AiProviderType.Ollama;

    public async Task<AiProviderEmbeddingResponse> ExecuteAsync(
        AiProviderEmbeddingRequest request,
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        var model = ProviderGuards.RequireExternalModelId(context);

        var url = ProviderUrl.Combine(context.BaseUrl, "api/embed");

        var payload = OllamaRequestMapper.CreateEmbeddingPayload(request, model);

        using var httpRequest = ProviderHttp.CreateJsonRequest(
            HttpMethod.Post,
            url,
            payload,
            context.Secret
        );

        var client = httpClientFactory.CreateClient(AiHttpClientNames.Ollama);

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

        if (
            root.TryGetProperty("embeddings", out var vectors)
            && vectors.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var vector in vectors.EnumerateArray())
            {
                if (vector.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                embeddings.Add(
                    vector.EnumerateArray().Select(value => value.GetSingle()).ToArray()
                );
            }
        }

        var inputTokens = ProviderJson.GetInt32(root, "prompt_eval_count");

        return new AiProviderEmbeddingResponse(
            embeddings,
            embeddings.FirstOrDefault()?.Count ?? 0,
            inputTokens,
            root.GetRawText()
        );
    }
}
