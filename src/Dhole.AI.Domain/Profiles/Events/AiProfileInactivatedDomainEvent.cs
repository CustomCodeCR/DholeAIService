using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Profiles.Events;

public sealed record AiProfileInactivatedDomainEvent(Guid id, string key, Guid? updatedBy)
    : DomainEvent;
