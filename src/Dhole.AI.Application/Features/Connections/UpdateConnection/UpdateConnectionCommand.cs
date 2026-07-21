using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Features.Connections.Update;

public sealed record UpdateConnectionCommand(
    Guid Id,
    string Name,
    AiProviderType ProviderType,
    string BaseUrl,
    string? SecretReference,
    int TimeoutSeconds,
    Guid? UpdatedBy
) : ICommand<Result>;
