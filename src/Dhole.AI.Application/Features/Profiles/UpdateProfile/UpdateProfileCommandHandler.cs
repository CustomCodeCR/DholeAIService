using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Profiles.Update;

public sealed class UpdateProfileCommandHandler(
    IAiProfileRepository profiles,
    IAiPromptTemplateRepository promptTemplates,
    IAiAuditService audit,
    IAiProfileCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateProfileCommand, Result>
{
    public async Task<Result> HandleAsync(
        UpdateProfileCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var profile = await profiles.GetByIdAsync(command.Id, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            return Result.Failure(AiErrors.ProfileNotFound);
        }

        if (await profiles.ExistsByKeyAsync(command.Key, command.Id, cancellationToken))
        {
            return Result.Failure(AiApplicationErrors.ProfileAlreadyExists);
        }

        if (command.PromptTemplateId.HasValue)
        {
            var template = await promptTemplates.GetByIdAsync(
                command.PromptTemplateId.Value,
                cancellationToken
            );

            if (template is null || template.IsDeleted || !template.IsActive)
            {
                return Result.Failure(AiErrors.PromptTemplateNotFound);
            }
        }

        var previousKey = profile.Key;
        var before = AiAuditSnapshots.From(profile);

        try
        {
            profile.Update(
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
                command.UpdatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(AiApplicationErrors.InvalidProfile);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ProfileUpdated,
                Action: AiAuditActions.Updated,
                EntityType: AiAuditEntityTypes.Profile,
                EntityId: profile.Id,
                ActorUserId: command.UpdatedBy,
                Before: before,
                After: AiAuditSnapshots.From(profile),
                Payload: AiAuditSnapshots.From(profile)
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveProfileCacheAsync(profile.Id, previousKey, cancellationToken);

        if (!string.Equals(previousKey, profile.Key, StringComparison.OrdinalIgnoreCase))
        {
            await cache.RemoveByKeyAsync(profile.Key, cancellationToken);
        }

        return Result.Success();
    }
}
