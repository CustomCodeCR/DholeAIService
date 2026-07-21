using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Profiles.Create;

public sealed class CreateProfileCommandHandler(
    IAiProfileRepository profiles,
    IAiModelRepository models,
    IAiPromptTemplateRepository promptTemplates,
    IAiAuditService audit,
    IAiProfileCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<CreateProfileCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateProfileCommand command,
        CancellationToken cancellationToken = default
    )
    {
        if (await profiles.ExistsByKeyAsync(command.Key, cancellationToken: cancellationToken))
        {
            return Result.Failure<Guid>(AiApplicationErrors.ProfileAlreadyExists);
        }

        if (
            command.PromptTemplateId.HasValue
            && !await PromptTemplateExistsAsync(command.PromptTemplateId.Value, cancellationToken)
        )
        {
            return Result.Failure<Guid>(AiErrors.PromptTemplateNotFound);
        }

        var modelValidation = await ValidateModelsAsync(
            command.Models.Select(item => item.ModelId).ToArray(),
            cancellationToken
        );

        if (modelValidation.IsFailure)
        {
            return Result.Failure<Guid>(modelValidation.Error);
        }

        AiProfile profile;

        try
        {
            profile = AiProfile.Create(
                command.Key,
                command.Name,
                command.Description,
                command.PromptTemplateId,
                command.RoutingMode,
                command.ResponseFormat,
                command.Temperature,
                command.MaximumOutputTokens,
                command.TimeoutSeconds,
                command.JsonSchema,
                command.CreatedBy
            );

            profile.ConfigureModels(
                command.Models.Select(item => (item.ModelId, item.Priority, item.IsFallback)),
                command.CreatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Guid>(AiApplicationErrors.InvalidProfile);
        }

        await profiles.AddAsync(profile, cancellationToken);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ProfileCreated,
                Action: AiAuditActions.Created,
                EntityType: AiAuditEntityTypes.Profile,
                EntityId: profile.Id,
                ActorUserId: command.CreatedBy,
                After: AiAuditSnapshots.From(profile),
                Payload: AiAuditSnapshots.From(profile)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveProfileCacheAsync(profile.Id, profile.Key, cancellationToken);

        return Result.Success(profile.Id);
    }

    private async Task<bool> PromptTemplateExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await promptTemplates.GetByIdAsync(id, cancellationToken);

        return template is not null && !template.IsDeleted && template.IsActive;
    }

    private async Task<Result> ValidateModelsAsync(
        IReadOnlyCollection<Guid> modelIds,
        CancellationToken cancellationToken
    )
    {
        if (modelIds.Count == 0)
        {
            return Result.Failure(AiApplicationErrors.ProfileHasInvalidModels);
        }

        var found = await models.GetByIdsAsync(modelIds, cancellationToken);

        if (
            found.Count != modelIds.Distinct().Count()
            || found.Any(item => item.IsDeleted || !item.IsActive)
        )
        {
            return Result.Failure(AiApplicationErrors.ProfileHasInvalidModels);
        }

        return Result.Success();
    }
}
