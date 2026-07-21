using CustomCodeFramework.Redis.Abstractions;
using CustomCodeFramework.Redis.Caching;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Contracts.Models.Response;

namespace Dhole.AI.Infrastructure.Cache;

public sealed class AiModelCacheService(ICacheService cache) : IAiModelCacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan VersionExpiration = TimeSpan.FromDays(3650);

    public Task<AiModelDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.GetAsync<AiModelDto>(AiModelCacheKeys.ById(id), cancellationToken);
    }

    public Task SetByIdAsync(
        Guid id,
        AiModelDto model,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            AiModelCacheKeys.ById(id),
            model,
            CacheEntryOptions.Default(expiration ?? DefaultExpiration),
            cancellationToken
        );
    }

    public Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(AiModelCacheKeys.ById(id), cancellationToken);
    }

    public async Task RemoveModelCacheAsync(
        Guid id,
        Guid connectionId,
        CancellationToken cancellationToken = default
    )
    {
        await RemoveByIdAsync(id, cancellationToken);

        await cache.SetAsync(
            AiModelCacheKeys.ConnectionModelsVersion(connectionId),
            Guid.NewGuid().ToString("N"),
            CacheEntryOptions.Default(VersionExpiration),
            cancellationToken
        );
    }
}
