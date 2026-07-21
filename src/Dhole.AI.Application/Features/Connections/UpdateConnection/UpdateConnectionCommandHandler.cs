using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Connections.Update;

public sealed class UpdateConnectionCommandHandler(
    IAiConnectionRepository connections,
    IAiAuditService audit,
    IAiConnectionCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateConnectionCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdateConnectionCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await connections.GetByIdAsync(command.Id, cancellationToken);

        if (connection is null || connection.IsDeleted)
        {
            return Result.Failure(AiErrors.ConnectionNotFound);
        }

        if (await connections.ExistsByNameAsync(command.Name, command.Id, cancellationToken))
        {
            return Result.Failure(AiApplicationErrors.ConnectionAlreadyExists);
        }

        var before = AiAuditSnapshots.From(connection);

        try
        {
            connection.Update(
                command.Name,
                command.ProviderType,
                command.BaseUrl,
                command.SecretReference,
                command.TimeoutSeconds,
                command.UpdatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(AiApplicationErrors.InvalidConnection);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ConnectionUpdated,
                Action: AiAuditActions.Updated,
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
