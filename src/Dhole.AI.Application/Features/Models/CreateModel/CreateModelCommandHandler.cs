using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Models.Create;

public sealed class CreateModelCommandHandler(
    IAiConnectionRepository connections,
    IAiModelRepository models,
    IAiAuditService audit,
    IAiModelCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateModelCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateModelCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await connections.GetByIdAsync(command.ConnectionId, cancellationToken);

        if (connection is null || connection.IsDeleted)
        {
            return Result.Failure<Guid>(AiErrors.ConnectionNotFound);
        }

        if (
            await models.ExistsByExternalModelIdAsync(
                command.ConnectionId,
                command.ExternalModelId,
                cancellationToken: cancellationToken
            )
        )
        {
            return Result.Failure<Guid>(AiApplicationErrors.ModelAlreadyExists);
        }

        AiModel model;

        try
        {
            model = AiModel.Create(
                command.ConnectionId,
                command.ExternalModelId,
                command.Name,
                command.Capabilities,
                command.ContextWindow,
                command.MaximumOutputTokens,
                command.InputCostPerMillionTokens,
                command.OutputCostPerMillionTokens,
                command.IsLocal,
                command.CreatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(AiApplicationErrors.InvalidModel);
        }

        await models.AddAsync(model, cancellationToken);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ModelCreated,
                Action: AiAuditActions.Created,
                EntityType: AiAuditEntityTypes.Model,
                EntityId: model.Id,
                ActorUserId: command.CreatedBy,
                After: AiAuditSnapshots.From(model),
                Payload: AiAuditSnapshots.From(model)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveModelCacheAsync(model.Id, model.ConnectionId, cancellationToken);

        return Result.Success(model.Id);
    }
}
