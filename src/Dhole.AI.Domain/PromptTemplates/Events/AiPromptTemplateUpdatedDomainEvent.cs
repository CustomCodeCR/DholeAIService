using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.PromptTemplates.Events;

public sealed record AiPromptTemplateUpdatedDomainEvent(
    Guid id,
    string key,
    string name,
    Guid? updatedBy
) : DomainEvent;
