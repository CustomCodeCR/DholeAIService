using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Models.Events;

public sealed record AiModelInactivatedDomainEvent(
    Guid id,
    Guid connectionId,
    string name,
    Guid? updatedBy
) : DomainEvent;
