using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Profiles.Response;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Profiles.GetByKey;

public sealed class GetProfileByKeyQueryHandler(
    IAiProfileRepository profiles,
    IAiProfileCacheService cache
) : IQueryHandler<GetProfileByKeyQuery, Result<AiProfileDto>>
{
    public async Task<Result<AiProfileDto>> HandleAsync(
        GetProfileByKeyQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var key = query.Key.Trim().ToLowerInvariant();

        var cached = await cache.GetByKeyAsync(key, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var profile = await profiles.GetDtoByKeyAsync(key, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<AiProfileDto>(AiErrors.ProfileNotFound);
        }

        await cache.SetByIdAsync(profile.Id, profile, cancellationToken: cancellationToken);

        await cache.SetByKeyAsync(profile.Key, profile, cancellationToken: cancellationToken);

        return Result.Success(profile);
    }
}
