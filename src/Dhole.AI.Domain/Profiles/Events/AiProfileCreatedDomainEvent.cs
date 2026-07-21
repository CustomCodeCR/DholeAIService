using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Profiles.Events;

public sealed record AiProfileCreatedDomainEvent(Guid id, string key, string name, Guid? createdBy)
    : DomainEvent;
