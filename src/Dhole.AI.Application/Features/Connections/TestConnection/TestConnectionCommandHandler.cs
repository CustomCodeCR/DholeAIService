using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Contracts.Connections.Response;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Connections.Test;

public sealed class TestConnectionCommandHandler(
    IAiConnectionRepository connections,
    IAiProviderResolver providers,
    IAiSecretResolver secrets,
    IAiAuditService audit,
    IAiConnectionCacheService cache,
    IUnitOfWork unitOfWork
) : ICommandHandler<TestConnectionCommand, Result<AiConnectionTestResultDto>>
{
    public async Task<Result<AiConnectionTestResultDto>> HandleAsync(
        TestConnectionCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await connections.GetByIdAsync(command.Id, cancellationToken);

        if (connection is null || connection.IsDeleted)
        {
            return Result.Failure<AiConnectionTestResultDto>(AiErrors.ConnectionNotFound);
        }

        AiProviderHealthResult result;

        try
        {
            var secret = await secrets.ResolveAsync(connection.SecretReference, cancellationToken);

            var context = new AiProviderContext(
                connection.Id,
                connection.Name,
                connection.ProviderType,
                connection.BaseUrl,
                secret,
                connection.TimeoutSeconds
            );

            var checker = providers.ResolveHealthChecker(connection.ProviderType);

            result = await checker.CheckAsync(context, cancellationToken);
        }
        catch (Exception exception)
        {
            result = new AiProviderHealthResult(
                false,
                0,
                DateTime.UtcNow,
                "AI.ConnectionTestFailed",
                exception.Message
            );
        }

        var before = AiAuditSnapshots.From(connection);

        if (result.Success)
        {
            connection.MarkHealthy(result.CheckedAtUtc);
        }
        else
        {
            connection.MarkUnhealthy(
                result.CheckedAtUtc,
                result.ErrorMessage ?? "No fue posible establecer la conexión."
            );
        }

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.ConnectionTested,
                Action: AiAuditActions.Tested,
                EntityType: AiAuditEntityTypes.Connection,
                EntityId: connection.Id,
                ActorUserId: command.TestedBy,
                Before: before,
                After: AiAuditSnapshots.From(connection),
                Payload: result,
                ErrorMessage: result.ErrorMessage
            ),
            cancellationToken
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveConnectionCacheAsync(connection.Id, cancellationToken);

        return Result.Success(
            new AiConnectionTestResultDto(
                connection.Id,
                result.Success,
                connection.Status.ToString(),
                result.DurationMilliseconds,
                result.CheckedAtUtc,
                result.ErrorCode,
                result.ErrorMessage
            )
        );
    }
}
