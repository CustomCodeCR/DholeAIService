using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Models.Events;

public sealed record AiModelCreatedDomainEvent(
    Guid id,
    Guid connectionId,
    string name,
    Guid? createdBy
) : DomainEvent;
