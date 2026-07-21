using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Models.Delete;

public sealed class DeleteModelCommandHandler(
    IAiModelRepository models,
    IAiAuditService audit,
    IAiModelCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<DeleteModelCommand, Result>
{
    public async Task<Result> HandleAsync(
        DeleteModelCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var model = await models.GetByIdAsync(command.Id, cancellationToken);

        if (model is null || model.IsDeleted)
        {
            return Result.Failure(AiErrors.ModelNotFound);
        }

        var before = AiAuditSnapshots.From(model);

        model.Delete(command.DeletedBy);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ModelDeleted,
                Action: AiAuditActions.Deleted,
                EntityType: AiAuditEntityTypes.Model,
                EntityId: model.Id,
                ActorUserId: command.DeletedBy,
                Before: before,
                After: AiAuditSnapshots.From(model),
                Payload: AiAuditSnapshots.From(model)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveModelCacheAsync(model.Id, model.ConnectionId, cancellationToken);

        return Result.Success();
    }
}
