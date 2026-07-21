using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Profiles.Response;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Profiles.GetById;

public sealed class GetProfileByIdQueryHandler(
    IAiProfileRepository profiles,
    IAiProfileCacheService cache
) : IQueryHandler<GetProfileByIdQuery, Result<AiProfileDto>>
{
    public async Task<Result<AiProfileDto>> HandleAsync(
        GetProfileByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var cached = await cache.GetByIdAsync(query.Id, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var profile = await profiles.GetDtoByIdAsync(query.Id, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<AiProfileDto>(AiErrors.ProfileNotFound);
        }

        await cache.SetByIdAsync(profile.Id, profile, cancellationToken: cancellationToken);

        await cache.SetByKeyAsync(profile.Key, profile, cancellationToken: cancellationToken);

        return Result.Success(profile);
    }
}
