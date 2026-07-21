using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Models.Update;

public sealed class UpdateModelCommandHandler(
    IAiConnectionRepository connections,
    IAiModelRepository models,
    IAiAuditService audit,
    IAiModelCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateModelCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdateModelCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var model = await models.GetByIdAsync(command.Id, cancellationToken);

        if (model is null || model.IsDeleted)
        {
            return Result.Failure(AiErrors.ModelNotFound);
        }

        var connection = await connections.GetByIdAsync(command.ConnectionId, cancellationToken);

        if (connection is null || connection.IsDeleted)
        {
            return Result.Failure(AiErrors.ConnectionNotFound);
        }

        if (
            await models.ExistsByExternalModelIdAsync(
                command.ConnectionId,
                command.ExternalModelId,
                command.Id,
                cancellationToken
            )
        )
        {
            return Result.Failure(AiApplicationErrors.ModelAlreadyExists);
        }

        var previousConnectionId = model.ConnectionId;
        var before = AiAuditSnapshots.From(model);

        try
        {
            model.Update(
                command.ConnectionId,
                command.ExternalModelId,
                command.Name,
                command.Capabilities,
                command.ContextWindow,
                command.MaximumOutputTokens,
                command.InputCostPerMillionTokens,
                command.OutputCostPerMillionTokens,
                command.IsLocal,
                command.UpdatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(AiApplicationErrors.InvalidModel);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ModelUpdated,
                Action: AiAuditActions.Updated,
                EntityType: AiAuditEntityTypes.Model,
                EntityId: model.Id,
                ActorUserId: command.UpdatedBy,
                Before: before,
                After: AiAuditSnapshots.From(model),
                Payload: AiAuditSnapshots.From(model)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveModelCacheAsync(model.Id, model.ConnectionId, cancellationToken);

        if (previousConnectionId != model.ConnectionId)
        {
            await cache.RemoveModelCacheAsync(model.Id, previousConnectionId, cancellationToken);
        }

        return Result.Success();
    }
}
