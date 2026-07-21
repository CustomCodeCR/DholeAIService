using CustomCodeFramework.Core.Domain.Events;
using Dhole.AI.Domain.Connections.Enums;

namespace Dhole.AI.Domain.Connections.Events;

public sealed record AiConnectionUpdatedDomainEvent(
    Guid id,
    string name,
    AiProviderType providerType,
    Guid? updatedBy
) : DomainEvent;
