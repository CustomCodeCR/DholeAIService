using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Abstractions.Providers;

public interface IAiModelDiscoveryProvider
{
    AiProviderType ProviderType { get; }

    Task<IReadOnlyCollection<AiDiscoveredModel>> DiscoverAsync(
        AiProviderContext context,
        CancellationToken cancellationToken = default
    );
}
