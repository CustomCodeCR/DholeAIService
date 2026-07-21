using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Connections.Delete;

public sealed class DeleteConnectionCommandHandler(
    IAiConnectionRepository connections,
    IAiAuditService audit,
    IAiConnectionCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<DeleteConnectionCommand, Result>
{
    public async Task<Result> HandleAsync(
        DeleteConnectionCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await connections.GetByIdAsync(command.Id, cancellationToken);

        if (connection is null || connection.IsDeleted)
        {
            return Result.Failure(AiErrors.ConnectionNotFound);
        }

        var before = AiAuditSnapshots.From(connection);

        connection.Delete(command.DeletedBy);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ConnectionDeleted,
                Action: AiAuditActions.Deleted,
                EntityType: AiAuditEntityTypes.Connection,
                EntityId: connection.Id,
                ActorUserId: command.DeletedBy,
                Before: before,
                After: AiAuditSnapshots.From(connection),
                Payload: AiAuditSnapshots.From(connection)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveConnectionCacheAsync(connection.Id, cancellationToken);

        return Result.Success();
    }
}
