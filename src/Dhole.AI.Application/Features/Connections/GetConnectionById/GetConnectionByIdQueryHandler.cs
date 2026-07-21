using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Connections.Response;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Connections.GetById;

public sealed class GetConnectionByIdQueryHandler(
    IAiConnectionRepository connections,
    IAiConnectionCacheService cache
) : IQueryHandler<GetConnectionByIdQuery, Result<AiConnectionDto>>
{
    public async Task<Result<AiConnectionDto>> HandleAsync(
        GetConnectionByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var cached = await cache.GetByIdAsync(query.Id, cancellationToken);

        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var connection = await connections.GetDtoByIdAsync(query.Id, cancellationToken);

        if (connection is null)
        {
            return Result.Failure<AiConnectionDto>(AiErrors.ConnectionNotFound);
        }

        await cache.SetByIdAsync(connection.Id, connection, cancellationToken: cancellationToken);

        return Result.Success(connection);
    }
}
