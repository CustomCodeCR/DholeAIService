using Dhole.AI.Contracts.Connections.Response;

namespace Dhole.AI.Application.Abstractions.Cache;

public interface IAiConnectionCacheService
{
    Task<AiConnectionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetByIdAsync(
        Guid id,
        AiConnectionDto connection,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task RemoveConnectionCacheAsync(Guid id, CancellationToken cancellationToken = default);
}
