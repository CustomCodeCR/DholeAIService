using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.PromptTemplates.Events;

public sealed record AiPromptTemplateCreatedDomainEvent(
    Guid id,
    string key,
    string name,
    Guid? createdBy
) : DomainEvent;
