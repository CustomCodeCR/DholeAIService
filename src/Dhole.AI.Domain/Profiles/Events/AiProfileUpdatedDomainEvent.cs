using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Profiles.Events;

public sealed record AiProfileUpdatedDomainEvent(Guid id, string key, string name, Guid? updatedBy)
    : DomainEvent;
