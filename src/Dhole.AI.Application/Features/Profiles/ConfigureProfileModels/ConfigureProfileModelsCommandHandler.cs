using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Profiles.ConfigureModels;

public sealed class ConfigureProfileModelsCommandHandler(
    IAiProfileRepository profiles,
    IAiModelRepository models,
    IAiAuditService audit,
    IAiProfileCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<ConfigureProfileModelsCommand, Result>
{
    public async Task<Result> HandleAsync(
        ConfigureProfileModelsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var profile = await profiles.GetByIdAsync(command.Id, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            return Result.Failure(AiErrors.ProfileNotFound);
        }

        var modelIds = command.Models.Select(item => item.ModelId).Distinct().ToArray();

        var foundModels = await models.GetByIdsAsync(modelIds, cancellationToken);

        if (
            foundModels.Count != modelIds.Length
            || foundModels.Any(item => item.IsDeleted || !item.IsActive)
        )
        {
            return Result.Failure(AiApplicationErrors.ProfileHasInvalidModels);
        }

        var before = AiAuditSnapshots.From(profile);

        try
        {
            profile.ConfigureModels(
                command.Models.Select(item => (item.ModelId, item.Priority, item.IsFallback)),
                command.UpdatedBy
            );
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(AiApplicationErrors.InvalidProfile);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ProfileModelsConfigured,
                Action: AiAuditActions.Configured,
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

        await cache.RemoveProfileCacheAsync(profile.Id, profile.Key, cancellationToken);

        return Result.Success();
    }
}
