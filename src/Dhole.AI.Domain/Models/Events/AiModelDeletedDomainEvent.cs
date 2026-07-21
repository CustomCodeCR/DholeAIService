using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Models.Events;

public sealed record AiModelDeletedDomainEvent(
    Guid id,
    Guid connectionId,
    string name,
    Guid? deletedBy
) : DomainEvent;
