using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Models.Response;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Models.GetById;

public sealed class GetModelByIdQueryHandler(IAiModelRepository models, IAiModelCacheService cache)
    : IQueryHandler<GetModelByIdQuery, Result<AiModelDto>>
{
    public async Task<Result<AiModelDto>> HandleAsync(
        GetModelByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var cached = await cache.GetByIdAsync(query.Id, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var model = await models.GetDtoByIdAsync(query.Id, cancellationToken);

        if (model is null)
        {
            return Result.Failure<AiModelDto>(AiErrors.ModelNotFound);
        }

        await cache.SetByIdAsync(model.Id, model, cancellationToken: cancellationToken);

        return Result.Success(model);
    }
}
