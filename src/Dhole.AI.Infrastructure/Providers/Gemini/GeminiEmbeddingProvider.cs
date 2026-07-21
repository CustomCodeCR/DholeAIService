using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Gemini;

public sealed class GeminiEmbeddingProvider(IHttpClientFactory httpClientFactory)
    : IAiEmbeddingProvider
{
    public AiProviderType ProviderType => AiProviderType.Gemini;

    public async Task<AiProviderEmbeddingResponse> ExecuteAsync(
        AiProviderEmbeddingRequest request,
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(context.Secret))
        {
            throw new InvalidOperationException("La API key de Gemini es obligatoria.");
        }

        var model = ProviderGuards.RequireExternalModelId(context);

        var modelId = GeminiRequestMapper.NormalizeModelId(model);

        var modelResourceName = GeminiRequestMapper.NormalizeModelResourceName(modelId);

        var url = ProviderUrl.Api(
            context.BaseUrl,
            "v1beta",
            $"models/{Uri.EscapeDataString(modelId)}:" + "batchEmbedContents"
        );

        var payload = GeminiRequestMapper.CreateEmbeddingPayload(request, modelResourceName);

        using var httpRequest = ProviderHttp.CreateJsonRequest(
            HttpMethod.Post,
            url,
            payload,
            headers: new Dictionary<string, string> { ["x-goog-api-key"] = context.Secret }
        );

        var client = httpClientFactory.CreateClient(AiHttpClientNames.Gemini);

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
            root.TryGetProperty("embeddings", out var values)
            && values.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var embedding in values.EnumerateArray())
            {
                if (
                    !embedding.TryGetProperty("values", out var vector)
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

        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            inputTokens = ProviderJson.GetInt32(usage, "promptTokenCount");
        }

        return new AiProviderEmbeddingResponse(
            embeddings,
            embeddings.FirstOrDefault()?.Count ?? 0,
            inputTokens,
            root.GetRawText()
        );
    }
}
