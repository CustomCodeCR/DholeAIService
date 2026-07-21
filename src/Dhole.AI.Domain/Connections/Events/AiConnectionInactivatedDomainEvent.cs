using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Connections.Events;

public sealed record AiConnectionInactivatedDomainEvent(Guid id, string name, Guid? updatedBy)
    : DomainEvent;
