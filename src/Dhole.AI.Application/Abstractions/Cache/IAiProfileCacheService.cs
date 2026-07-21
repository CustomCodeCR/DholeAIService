using Dhole.AI.Contracts.Profiles.Response;

namespace Dhole.AI.Application.Abstractions.Cache;

public interface IAiProfileCacheService
{
    Task<AiProfileDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetByIdAsync(
        Guid id,
        AiProfileDto profile,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task<AiProfileDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task SetByKeyAsync(
        string key,
        AiProfileDto profile,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task RemoveByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveProfileCacheAsync(
        Guid id,
        string key,
        CancellationToken cancellationToken = default
    );
}
