using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.PromptTemplates.Delete;

public sealed class DeletePromptTemplateCommandHandler(
    IAiPromptTemplateRepository templates,
    IAiAuditService audit,
    IAiPromptTemplateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<DeletePromptTemplateCommand, Result>
{
    public async Task<Result> HandleAsync(
        DeletePromptTemplateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var template = await templates.GetByIdAsync(command.Id, cancellationToken);

        if (template is null || template.IsDeleted)
        {
            return Result.Failure(AiErrors.PromptTemplateNotFound);
        }

        var before = AiAuditSnapshots.From(template);

        template.Delete(command.DeletedBy);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.PromptTemplateDeleted,
                Action: AiAuditActions.Deleted,
                EntityType: AiAuditEntityTypes.PromptTemplate,
                EntityId: template.Id,
                ActorUserId: command.DeletedBy,
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
