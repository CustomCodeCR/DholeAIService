using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Gemini;

public sealed class GeminiModelDiscoveryProvider(IHttpClientFactory httpClientFactory)
    : IAiModelDiscoveryProvider
{
    public AiProviderType ProviderType => AiProviderType.Gemini;

    public async Task<IReadOnlyCollection<AiDiscoveredModel>> DiscoverAsync(
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
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
                var resourceName = model.TryGetProperty("name", out var nameProperty)
                    ? nameProperty.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(resourceName))
                {
                    continue;
                }

                var id = GeminiRequestMapper.NormalizeModelId(resourceName);

                var displayName = model.TryGetProperty("displayName", out var displayNameProperty)
                    ? displayNameProperty.GetString()
                    : id;

                var capabilities = ReadCapabilities(model);

                int? contextWindow =
                    model.TryGetProperty("inputTokenLimit", out var inputLimit)
                    && inputLimit.TryGetInt32(out var contextValue)
                        ? contextValue
                        : null;

                int? maximumOutputTokens =
                    model.TryGetProperty("outputTokenLimit", out var outputLimit)
                    && outputLimit.TryGetInt32(out var outputValue)
                        ? outputValue
                        : null;

                result.Add(
                    new AiDiscoveredModel(
                        id,
                        displayName ?? id,
                        capabilities,
                        contextWindow,
                        maximumOutputTokens,
                        false
                    )
                );
            }
        }

        return result;
    }

    private static AiModelCapability ReadCapabilities(JsonElement model)
    {
        var result = AiModelCapability.None;

        var methodsPropertyName = model.TryGetProperty("supportedGenerationMethods", out _)
            ? "supportedGenerationMethods"
            : "supportedActions";

        if (
            !model.TryGetProperty(methodsPropertyName, out var methods)
            || methods.ValueKind != JsonValueKind.Array
        )
        {
            return result;
        }

        var values = methods
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (values.Contains("generateContent"))
        {
            result |= AiModelCapability.Chat;
            result |= AiModelCapability.Streaming;
            result |= AiModelCapability.StructuredOutput;
        }

        if (values.Contains("embedContent") || values.Contains("batchEmbedContents"))
        {
            result |= AiModelCapability.Embeddings;
        }

        return result;
    }
}
