using System.Text.Json;
using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.PromptTemplates.Entities;

namespace Dhole.AI.Application.Features.PromptTemplates.Create;

public sealed class CreatePromptTemplateCommandHandler(
    IAiPromptTemplateRepository templates,
    IAiAuditService audit,
    IAiPromptTemplateCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreatePromptTemplateCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreatePromptTemplateCommand command,
        CancellationToken cancellationToken = default
    )
    {
        if (await templates.ExistsByKeyAsync(command.Key, cancellationToken: cancellationToken))
        {
            return Result.Failure<Guid>(AiApplicationErrors.PromptTemplateAlreadyExists);
        }

        AiPromptTemplate template;

        try
        {
            template = AiPromptTemplate.Create(
                command.Key,
                command.Name,
                command.Description,
                command.SystemPrompt,
                command.UserPromptTemplate,
                JsonSerializer.Serialize(
                    command.Variables.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ),
                command.CreatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(AiApplicationErrors.InvalidPromptTemplate);
        }

        await templates.AddAsync(template, cancellationToken);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.PromptTemplateCreated,
                Action: AiAuditActions.Created,
                EntityType: AiAuditEntityTypes.PromptTemplate,
                EntityId: template.Id,
                ActorUserId: command.CreatedBy,
                After: AiAuditSnapshots.From(template),
                Payload: AiAuditSnapshots.From(template)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemovePromptTemplateCacheAsync(template.Id, cancellationToken);

        return Result.Success(template.Id);
    }
}
