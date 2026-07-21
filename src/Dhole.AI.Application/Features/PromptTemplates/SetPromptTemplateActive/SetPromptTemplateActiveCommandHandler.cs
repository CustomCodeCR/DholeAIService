using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.PromptTemplates.SetActive;

public sealed class SetPromptTemplateActiveCommandHandler(
    IAiPromptTemplateRepository templates,
    IAiAuditService audit,
    IAiPromptTemplateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<SetPromptTemplateActiveCommand, Result>
{
    public async Task<Result> HandleAsync(
        SetPromptTemplateActiveCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var template = await templates.GetByIdAsync(command.Id, cancellationToken);

        if (template is null || template.IsDeleted)
        {
            return Result.Failure(AiErrors.PromptTemplateNotFound);
        }

        if (template.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        var before = AiAuditSnapshots.From(template);

        if (command.IsActive)
        {
            template.Activate(command.UpdatedBy);
        }
        else
        {
            template.Inactivate(command.UpdatedBy);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: command.IsActive
                    ? AiAuditEventTypes.PromptTemplateActivated
                    : AiAuditEventTypes.PromptTemplateInactivated,
                Action: command.IsActive ? AiAuditActions.Activated : AiAuditActions.Inactivated,
                EntityType: AiAuditEntityTypes.PromptTemplate,
                EntityId: template.Id,
                ActorUserId: command.UpdatedBy,
                Before: before,
                After: AiAuditSnapshots.From(template),
                Payload: AiAuditSnapshots.From(template)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemovePromptTemplateCacheAsync(template.Id, cancellationToken);

        return Result.Success();
    }
}
