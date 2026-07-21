using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Abstractions.Providers;

public interface IAiChatProvider
{
    AiProviderType ProviderType { get; }

    Task<AiProviderChatResponse> ExecuteAsync(
        AiProviderChatRequest request,
        AiProviderContext context,
        CancellationToken cancellationToken = default
    );

    IAsyncEnumerable<string> ExecuteStreamAsync(
        AiProviderChatRequest request,
        AiProviderContext context,
        CancellationToken cancellationToken = default
    );
}
