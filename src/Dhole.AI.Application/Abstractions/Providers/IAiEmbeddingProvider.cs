using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Abstractions.Providers;

public interface IAiEmbeddingProvider
{
    AiProviderType ProviderType { get; }

    Task<AiProviderEmbeddingResponse> ExecuteAsync(
        AiProviderEmbeddingRequest request,
        AiProviderContext context,
        CancellationToken cancellationToken = default
    );
}
