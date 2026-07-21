using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Connections.Events;

public sealed record AiConnectionDeletedDomainEvent(Guid id, string name, Guid? deletedBy)
    : DomainEvent;
