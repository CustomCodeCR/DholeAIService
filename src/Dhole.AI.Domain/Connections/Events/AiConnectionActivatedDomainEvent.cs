using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Connections.Events;

public sealed record AiConnectionActivatedDomainEvent(Guid id, string name, Guid? updatedBy)
    : DomainEvent;
