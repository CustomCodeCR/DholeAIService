using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Profiles.Events;

public sealed record AiProfileDeletedDomainEvent(Guid id, string key, Guid? deletedBy)
    : DomainEvent;
