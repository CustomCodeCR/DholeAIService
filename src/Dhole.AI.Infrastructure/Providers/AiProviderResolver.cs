using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Infrastructure.Providers;

public sealed class AiProviderResolver(
    IEnumerable<IAiChatProvider> chatProviders,
    IEnumerable<IAiEmbeddingProvider> embeddingProviders,
    IEnumerable<IAiModelDiscoveryProvider> discoveryProviders,
    IEnumerable<IAiProviderHealthChecker> healthCheckers
) : IAiProviderResolver
{
    private readonly IReadOnlyDictionary<AiProviderType, IAiChatProvider> _chatProviders =
        CreateMap(chatProviders);

    private readonly IReadOnlyDictionary<AiProviderType, IAiEmbeddingProvider> _embeddingProviders =
        CreateMap(embeddingProviders);

    private readonly IReadOnlyDictionary<
        AiProviderType,
        IAiModelDiscoveryProvider
    > _discoveryProviders = CreateMap(discoveryProviders);

    private readonly IReadOnlyDictionary<AiProviderType, IAiProviderHealthChecker> _healthCheckers =
        CreateMap(healthCheckers);

    public IAiChatProvider ResolveChatProvider(AiProviderType providerType)
    {
        return Resolve(_chatProviders, providerType, "chat");
    }

    public IAiEmbeddingProvider ResolveEmbeddingProvider(AiProviderType providerType)
    {
        return Resolve(_embeddingProviders, providerType, "embeddings");
    }

    public IAiModelDiscoveryProvider ResolveModelDiscoveryProvider(AiProviderType providerType)
    {
        return Resolve(_discoveryProviders, providerType, "descubrimiento de modelos");
    }

    public IAiProviderHealthChecker ResolveHealthChecker(AiProviderType providerType)
    {
        return Resolve(_healthCheckers, providerType, "verificación de salud");
    }

    private static IReadOnlyDictionary<AiProviderType, TProvider> CreateMap<TProvider>(
        IEnumerable<TProvider> providers
    )
    {
        return providers.ToDictionary(GetProviderType);
    }

    private static AiProviderType GetProviderType<TProvider>(TProvider provider)
    {
        return provider switch
        {
            IAiChatProvider chat => chat.ProviderType,

            IAiEmbeddingProvider embedding => embedding.ProviderType,

            IAiModelDiscoveryProvider discovery => discovery.ProviderType,

            IAiProviderHealthChecker health => health.ProviderType,

            _ => throw new InvalidOperationException(
                $"No se puede determinar el tipo " + $"del proveedor {typeof(TProvider).Name}."
            ),
        };
    }

    private static TProvider Resolve<TProvider>(
        IReadOnlyDictionary<AiProviderType, TProvider> providers,
        AiProviderType providerType,
        string operation
    )
    {
        if (providers.TryGetValue(providerType, out var provider))
        {
            return provider;
        }

        throw new NotSupportedException(
            $"El proveedor {providerType} no soporta " + $"la operación de {operation}."
        );
    }
}
