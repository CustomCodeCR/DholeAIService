using System.Runtime.CompilerServices;
using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Ollama;

public sealed class OllamaChatProvider(IHttpClientFactory httpClientFactory) : IAiChatProvider
{
    public AiProviderType ProviderType => AiProviderType.Ollama;

    public async Task<AiProviderChatResponse> ExecuteAsync(
        AiProviderChatRequest request,
        AiProviderContext context,
        CancellationToken cancellationToken = default
    )
    {
        var model = ProviderGuards.RequireExternalModelId(context);

        var url = ProviderUrl.Combine(context.BaseUrl, "api/chat");

        var payload = OllamaRequestMapper.CreateChatPayload(request, model, stream: false);

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

        return OllamaResponseMapper.ToChatResponse(document.RootElement);
    }

    public async IAsyncEnumerable<string> ExecuteStreamAsync(
        AiProviderChatRequest request,
        AiProviderContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var model = ProviderGuards.RequireExternalModelId(context);

        var url = ProviderUrl.Combine(context.BaseUrl, "api/chat");

        var payload = OllamaRequestMapper.CreateChatPayload(request, model, stream: true);

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

        await foreach (var line in StreamingReaders.ReadNdjsonAsync(stream, timeout.Token))
        {
            using var document = JsonDocument.Parse(line);

            var delta = OllamaResponseMapper.ReadStreamDelta(document.RootElement);

            if (!string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }
    }
}
