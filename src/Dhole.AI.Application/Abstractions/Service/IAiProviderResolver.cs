using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Abstractions.Services;

public interface IAiProviderResolver
{
    IAiChatProvider ResolveChatProvider(AiProviderType providerType);

    IAiEmbeddingProvider ResolveEmbeddingProvider(AiProviderType providerType);

    IAiModelDiscoveryProvider ResolveModelDiscoveryProvider(AiProviderType providerType);

    IAiProviderHealthChecker ResolveHealthChecker(AiProviderType providerType);
}
