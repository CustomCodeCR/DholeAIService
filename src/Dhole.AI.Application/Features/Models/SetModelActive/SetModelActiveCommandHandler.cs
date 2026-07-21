using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Models.SetActive;

public sealed class SetModelActiveCommandHandler(
    IAiModelRepository models,
    IAiAuditService audit,
    IAiModelCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<SetModelActiveCommand, Result>
{
    public async Task<Result> HandleAsync(
        SetModelActiveCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var model = await models.GetByIdAsync(command.Id, cancellationToken);

        if (model is null || model.IsDeleted)
        {
            return Result.Failure(AiErrors.ModelNotFound);
        }

        if (model.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        var before = AiAuditSnapshots.From(model);

        if (command.IsActive)
        {
            model.Activate(command.UpdatedBy);
        }
        else
        {
            model.Inactivate(command.UpdatedBy);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: command.IsActive
                    ? AiAuditEventTypes.ModelActivated
                    : AiAuditEventTypes.ModelInactivated,
                Action: command.IsActive ? AiAuditActions.Activated : AiAuditActions.Inactivated,
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

        return Result.Success();
    }
}
