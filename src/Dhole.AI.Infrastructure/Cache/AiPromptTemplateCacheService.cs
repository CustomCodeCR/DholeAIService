using CustomCodeFramework.Redis.Abstractions;
using CustomCodeFramework.Redis.Caching;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Contracts.PromptTemplates.Response;

namespace Dhole.AI.Infrastructure.Cache;

public sealed class AiPromptTemplateCacheService(ICacheService cache)
    : IAiPromptTemplateCacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);

    public Task<AiPromptTemplateDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return cache.GetAsync<AiPromptTemplateDto>(
            AiPromptTemplateCacheKeys.ById(id),
            cancellationToken
        );
    }

    public Task SetByIdAsync(
        Guid id,
        AiPromptTemplateDto template,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        return cache.SetAsync(
            AiPromptTemplateCacheKeys.ById(id),
            template,
            CacheEntryOptions.Default(expiration ?? DefaultExpiration),
            cancellationToken
        );
    }

    public Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return cache.RemoveAsync(AiPromptTemplateCacheKeys.ById(id), cancellationToken);
    }

    public Task RemovePromptTemplateCacheAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return RemoveByIdAsync(id, cancellationToken);
    }
}
