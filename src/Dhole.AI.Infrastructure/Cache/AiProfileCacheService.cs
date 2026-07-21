using CustomCodeFramework.Redis.Abstractions;
using CustomCodeFramework.Redis.Caching;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Contracts.Profiles.Response;

namespace Dhole.AI.Infrastructure.Cache;

public sealed class AiProfileCacheService(ICacheService cache) : IAiProfileCacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);

    public Task<AiProfileDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<AiProfileDto>(AiProfileCacheKeys.ById(id), cancellationToken);
    }

    public Task SetByIdAsync(
        Guid id,
        AiProfileDto profile,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            AiProfileCacheKeys.ById(id),
            profile,
            CacheEntryOptions.Default(expiration ?? DefaultExpiration),
            cancellationToken
        );
    }

    public Task<AiProfileDto?> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAsync<AiProfileDto>(AiProfileCacheKeys.ByKey(key), cancellationToken);
    }

    public Task SetByKeyAsync(
        string key,
        AiProfileDto profile,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            AiProfileCacheKeys.ByKey(key),
            profile,
            CacheEntryOptions.Default(expiration ?? DefaultExpiration),
            cancellationToken
        );
    }

    public Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(AiProfileCacheKeys.ById(id), cancellationToken);
    }

    public Task RemoveByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(AiProfileCacheKeys.ByKey(key), cancellationToken);
    }

    public async Task RemoveProfileCacheAsync(
        Guid id,
        string key,
        CancellationToken cancellationToken = default
    )
    {
        await RemoveByIdAsync(id, cancellationToken);

        await RemoveByKeyAsync(key, cancellationToken);
    }
}
