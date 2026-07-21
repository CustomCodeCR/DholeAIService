using System.Text.Json;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.PromptTemplates.Update;

public sealed class UpdatePromptTemplateCommandHandler(
    IAiPromptTemplateRepository templates,
    IAiAuditService audit,
    IAiPromptTemplateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdatePromptTemplateCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdatePromptTemplateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var template = await templates.GetByIdAsync(command.Id, cancellationToken);

        if (template is null || template.IsDeleted)
        {
            return Result.Failure(AiErrors.PromptTemplateNotFound);
        }

        if (await templates.ExistsByKeyAsync(command.Key, command.Id, cancellationToken))
        {
            return Result.Failure(AiApplicationErrors.PromptTemplateAlreadyExists);
        }

        var before = AiAuditSnapshots.From(template);

        try
        {
            template.Update(
                command.Key,
                command.Name,
                command.Description,
                command.SystemPrompt,
                command.UserPromptTemplate,
                JsonSerializer.Serialize(
                    command.Variables.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ),
                command.UpdatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(AiApplicationErrors.InvalidPromptTemplate);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.PromptTemplateUpdated,
                Action: AiAuditActions.Updated,
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
