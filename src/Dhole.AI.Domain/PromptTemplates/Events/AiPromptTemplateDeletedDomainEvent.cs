using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.PromptTemplates.Events;

public sealed record AiPromptTemplateDeletedDomainEvent(Guid id, string key, Guid? deletedBy)
    : DomainEvent;
