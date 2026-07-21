using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Profiles.Delete;

public sealed class DeleteProfileCommandHandler(
    IAiProfileRepository profiles,
    IAiAuditService audit,
    IAiProfileCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<DeleteProfileCommand, Result>
{
    public async Task<Result> HandleAsync(
        DeleteProfileCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var profile = await profiles.GetByIdAsync(command.Id, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            return Result.Failure(AiErrors.ProfileNotFound);
        }

        var before = AiAuditSnapshots.From(profile);

        profile.Delete(command.DeletedBy);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ProfileDeleted,
                Action: AiAuditActions.Deleted,
                EntityType: AiAuditEntityTypes.Profile,
                EntityId: profile.Id,
                ActorUserId: command.DeletedBy,
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
