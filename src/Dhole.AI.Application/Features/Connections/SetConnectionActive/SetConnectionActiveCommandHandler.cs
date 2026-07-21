using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Connections.SetActive;

public sealed class SetConnectionActiveCommandHandler(
    IAiConnectionRepository connections,
    IAiAuditService audit,
    IAiConnectionCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<SetConnectionActiveCommand, Result>
{
    public async Task<Result> HandleAsync(
        SetConnectionActiveCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await connections.GetByIdAsync(command.Id, cancellationToken);

        if (connection is null || connection.IsDeleted)
        {
            return Result.Failure(AiErrors.ConnectionNotFound);
        }

        if (connection.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        var before = AiAuditSnapshots.From(connection);

        if (command.IsActive)
        {
            connection.Activate(command.UpdatedBy);
        }
        else
        {
            connection.Inactivate(command.UpdatedBy);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: command.IsActive
                    ? AiAuditEventTypes.ConnectionActivated
                    : AiAuditEventTypes.ConnectionInactivated,
                Action: command.IsActive ? AiAuditActions.Activated : AiAuditActions.Inactivated,
                EntityType: AiAuditEntityTypes.Connection,
                EntityId: connection.Id,
                ActorUserId: command.UpdatedBy,
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
