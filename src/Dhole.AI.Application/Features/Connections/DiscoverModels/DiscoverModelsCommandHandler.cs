using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Application.Shared;
using Dhole.AI.Contracts.Models.Response;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Application.Features.Connections.DiscoverModels;

public sealed class DiscoverModelsCommandHandler(
    IAiConnectionRepository connections,
    IAiModelRepository models,
    IAiProviderResolver providers,
    IAiSecretResolver secrets,
    IAiAuditService audit
) : ICommandHandler<DiscoverModelsCommand, Result<IReadOnlyCollection<DiscoveredAiModelDto>>>
{
    public async Task<Result<IReadOnlyCollection<DiscoveredAiModelDto>>> HandleAsync(
        DiscoverModelsCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await connections.GetByIdAsync(command.Id, cancellationToken);

        if (connection is null || connection.IsDeleted)
        {
            return Result.Failure<IReadOnlyCollection<DiscoveredAiModelDto>>(
                AiErrors.ConnectionNotFound
            );
        }

        if (!connection.IsActive)
        {
            return Result.Failure<IReadOnlyCollection<DiscoveredAiModelDto>>(
                AiApplicationErrors.ConnectionIsInactive
            );
        }

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

            var provider = providers.ResolveModelDiscoveryProvider(connection.ProviderType);

            var discovered = await provider.DiscoverAsync(context, cancellationToken);

            var registeredIds = await models.GetRegisteredExternalIdsAsync(
                connection.Id,
                cancellationToken
            );

            var result = discovered
                .OrderBy(item => item.Name)
                .Select(item => new DiscoveredAiModelDto(
                    item.ExternalModelId,
                    item.Name,
                    ConvertCapabilities(item.Capabilities),
                    item.ContextWindow,
                    item.MaximumOutputTokens,
                    item.IsLocal,
                    registeredIds.Contains(item.ExternalModelId),
                    null
                ))
                .ToArray();

            await audit.PublishAsync(
                new AiAuditEvent(
                    EventType: AiAuditEventTypes.ConnectionModelsDiscovered,
                    Action: AiAuditActions.Discovered,
                    EntityType: AiAuditEntityTypes.Connection,
                    EntityId: connection.Id,
                    ActorUserId: command.RequestedBy,
                    Payload: new
                    {
                        connection.Id,
                        Count = result.Length,
                        Models = result,
                    }
                ),
                cancellationToken
            );

            return Result.Success<IReadOnlyCollection<DiscoveredAiModelDto>>(result);
        }
        catch
        {
            return Result.Failure<IReadOnlyCollection<DiscoveredAiModelDto>>(
                AiApplicationErrors.ProviderOperationFailed
            );
        }
    }

    private static IReadOnlyCollection<string> ConvertCapabilities(
        AiModelCapability capabilities
    )
    {
        return Enum.GetValues<AiModelCapability>()
            .Where(value =>
                value != AiModelCapability.None && capabilities.HasFlag(value)
            )
            .Select(value => value.ToString())
            .ToArray();
    }
}
