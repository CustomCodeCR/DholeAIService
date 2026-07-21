using Dhole.AI.Contracts.Models.Response;

namespace Dhole.AI.Application.Abstractions.Cache;

public interface IAiModelCacheService
{
    Task<AiModelDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetByIdAsync(
        Guid id,
        AiModelDto model,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task RemoveModelCacheAsync(
        Guid id,
        Guid connectionId,
        CancellationToken cancellationToken = default
    );
}
