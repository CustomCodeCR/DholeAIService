using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.PromptTemplates.Events;

public sealed record AiPromptTemplateActivatedDomainEvent(Guid id, string key, Guid? updatedBy)
    : DomainEvent;
