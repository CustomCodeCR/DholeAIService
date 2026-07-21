using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Abstractions.Providers;

public interface IAiProviderHealthChecker
{
    AiProviderType ProviderType { get; }

    Task<AiProviderHealthResult> CheckAsync(
        AiProviderContext context,
        CancellationToken cancellationToken = default
    );
}
