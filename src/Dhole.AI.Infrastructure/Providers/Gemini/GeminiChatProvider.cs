using System.Runtime.CompilerServices;
using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Gemini;

public sealed class GeminiChatProvider(IHttpClientFactory httpClientFactory) : IAiChatProvider
{
    public AiProviderType ProviderType => AiProviderType.Gemini;

    public async Task<AiProviderChatResponse> ExecuteAsync(
        AiProviderChatRequest request,
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        var model = ProviderGuards.RequireExternalModelId(context);

        var modelId = GeminiRequestMapper.NormalizeModelId(model);

        var url = ProviderUrl.Api(
            context.BaseUrl,
            "v1beta",
            $"models/{Uri.EscapeDataString(modelId)}:" + "generateContent"
        );

        var payload = GeminiRequestMapper.CreateGeneratePayload(request);

        using var httpRequest = CreateRequest(HttpMethod.Post, url, payload, context.Secret);

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

        return GeminiResponseMapper.ToChatResponse(document.RootElement);
    }

    public async IAsyncEnumerable<string> ExecuteStreamAsync(
        AiProviderChatRequest request,
        AiProviderContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var model = ProviderGuards.RequireExternalModelId(context);

        var modelId = GeminiRequestMapper.NormalizeModelId(model);

        var url = ProviderUrl.Api(
            context.BaseUrl,
            "v1beta",
            $"models/{Uri.EscapeDataString(modelId)}:" + "streamGenerateContent?alt=sse"
        );

        var payload = GeminiRequestMapper.CreateGeneratePayload(request);

        using var httpRequest = CreateRequest(HttpMethod.Post, url, payload, context.Secret);

        var client = httpClientFactory.CreateClient(AiHttpClientNames.Gemini);

        using var timeout = ProviderHttp.CreateTimeout(context.TimeoutSeconds, cancellationToken);

        using var response = await client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );

        await ProviderHttp.EnsureSuccessAsync(response, timeout.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);

        await foreach (var data in StreamingReaders.ReadSseDataAsync(stream, timeout.Token))
        {
            using var document = JsonDocument.Parse(data);

            var text = GeminiResponseMapper.ReadText(document.RootElement);

            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        System.Text.Json.Nodes.JsonNode payload,
        string? apiKey
    )
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("La API key de Gemini es obligatoria.");
        }

        return ProviderHttp.CreateJsonRequest(
            method,
            url,
            payload,
            headers: new Dictionary<string, string> { ["x-goog-api-key"] = apiKey }
        );
    }
}
