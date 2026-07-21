using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Profiles.SetActive;

public sealed class SetProfileActiveCommandHandler(
    IAiProfileRepository profiles,
    IAiAuditService audit,
    IAiProfileCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<SetProfileActiveCommand, Result>
{
    public async Task<Result> HandleAsync(
        SetProfileActiveCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var profile = await profiles.GetByIdAsync(command.Id, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            return Result.Failure(AiErrors.ProfileNotFound);
        }

        if (profile.IsActive == command.IsActive)
        {
            return Result.Success();
        }

        var before = AiAuditSnapshots.From(profile);

        try
        {
            if (command.IsActive)
            {
                profile.Activate(command.UpdatedBy);
            }
            else
            {
                profile.Inactivate(command.UpdatedBy);
            }
        }
        catch (InvalidOperationException)
        {
            return Result.Failure(AiApplicationErrors.InvalidProfile);
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: command.IsActive
                    ? AiAuditEventTypes.ProfileActivated
                    : AiAuditEventTypes.ProfileInactivated,
                Action: command.IsActive ? AiAuditActions.Activated : AiAuditActions.Inactivated,
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
