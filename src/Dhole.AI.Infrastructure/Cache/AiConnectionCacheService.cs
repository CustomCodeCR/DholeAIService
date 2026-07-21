using CustomCodeFramework.Redis.Abstractions;
using CustomCodeFramework.Redis.Caching;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Contracts.Connections.Response;

namespace Dhole.AI.Infrastructure.Cache;

public sealed class AiConnectionCacheService(ICacheService cache) : IAiConnectionCacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);

    public Task<AiConnectionDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAsync<AiConnectionDto>(AiConnectionCacheKeys.ById(id), cancellationToken);
    }

    public Task SetByIdAsync(
        Guid id,
        AiConnectionDto connection,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            AiConnectionCacheKeys.ById(id),
            connection,
            CacheEntryOptions.Default(expiration ?? DefaultExpiration),
            cancellationToken
        );
    }

    public Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(AiConnectionCacheKeys.ById(id), cancellationToken);
    }

    public Task RemoveConnectionCacheAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return RemoveByIdAsync(id, cancellationToken);
    }
}
