using CustomCodeFramework.Core.Domain.Events;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Domain.Connections.Events;

public sealed record AiConnectionCreatedDomainEvent(
    Guid id,
    string name,
    AiProviderType providerType,
    Guid? createdBy
) : DomainEvent;
