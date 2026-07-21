using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Profiles.Events;

public sealed record AiProfileModelsChangedDomainEvent(
    Guid id,
    string key,
    IReadOnlyCollection<Guid> modelIds,
    Guid? updatedBy
) : DomainEvent;
