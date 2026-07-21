using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Connections.Entities;

namespace Dhole.AI.Application.Features.Connections.Create;

public sealed class CreateConnectionCommandHandler(
    IAiConnectionRepository connections,
    IAiAuditService audit,
    IAiConnectionCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateConnectionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateConnectionCommand command,
        CancellationToken cancellationToken = default
    )
    {
        if (await connections.ExistsByNameAsync(command.Name, cancellationToken: cancellationToken))
        {
            return Result.Failure<Guid>(AiApplicationErrors.ConnectionAlreadyExists);
        }

        AiConnection connection;

        try
        {
            connection = AiConnection.Create(
                command.Name,
                command.ProviderType,
                command.BaseUrl,
                command.SecretReference,
                command.TimeoutSeconds,
                command.CreatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(AiApplicationErrors.InvalidConnection);
        }

        await connections.AddAsync(connection, cancellationToken);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ConnectionCreated,
                Action: AiAuditActions.Created,
                EntityType: AiAuditEntityTypes.Connection,
                EntityId: connection.Id,
                ActorUserId: command.CreatedBy,
                After: AiAuditSnapshots.From(connection),
                Payload: AiAuditSnapshots.From(connection)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveConnectionCacheAsync(connection.Id, cancellationToken);

        return Result.Success(connection.Id);
    }
}
