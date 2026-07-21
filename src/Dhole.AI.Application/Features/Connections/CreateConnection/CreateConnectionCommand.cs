using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Application.Features.Connections.Create;

public sealed record CreateConnectionCommand(
    string Name,
    AiProviderType ProviderType,
    string BaseUrl,
    string? SecretReference,
    int TimeoutSeconds,
    Guid? CreatedBy
) : ICommand<Result<Guid>>;
