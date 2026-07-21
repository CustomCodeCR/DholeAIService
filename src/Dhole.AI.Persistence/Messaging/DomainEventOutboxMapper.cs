using CustomCodeFramework.Core.Domain.Events;
using Dhole.AI.Domain.Connections.Events;
using Dhole.AI.Domain.Executions.Events;
using Dhole.AI.Domain.Models.Events;
using Dhole.AI.Domain.Profiles.Events;
using Dhole.AI.Domain.PromptTemplates.Events;

namespace Dhole.AI.Persistence.Messaging;

internal static class DomainEventOutboxMapper
{
    public static string GetEventName(IDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            AiConnectionCreatedDomainEvent => "ai.connection.created",

            AiConnectionUpdatedDomainEvent => "ai.connection.updated",

            AiConnectionActivatedDomainEvent => "ai.connection.activated",

            AiConnectionInactivatedDomainEvent => "ai.connection.inactivated",

            AiConnectionDeletedDomainEvent => "ai.connection.deleted",

            AiModelCreatedDomainEvent => "ai.model.created",

            AiModelUpdatedDomainEvent => "ai.model.updated",

            AiModelActivatedDomainEvent => "ai.model.activated",

            AiModelInactivatedDomainEvent => "ai.model.inactivated",

            AiModelDeletedDomainEvent => "ai.model.deleted",

            AiProfileCreatedDomainEvent => "ai.profile.created",

            AiProfileUpdatedDomainEvent => "ai.profile.updated",

            AiProfileActivatedDomainEvent => "ai.profile.activated",

            AiProfileInactivatedDomainEvent => "ai.profile.inactivated",

            AiProfileDeletedDomainEvent => "ai.profile.deleted",

            AiProfileModelsChangedDomainEvent => "ai.profile.models-changed",

            AiPromptTemplateCreatedDomainEvent => "ai.prompt-template.created",

            AiPromptTemplateUpdatedDomainEvent => "ai.prompt-template.updated",

            AiPromptTemplateActivatedDomainEvent => "ai.prompt-template.activated",

            AiPromptTemplateInactivatedDomainEvent => "ai.prompt-template.inactivated",

            AiPromptTemplateDeletedDomainEvent => "ai.prompt-template.deleted",

            AiExecutionStartedDomainEvent => "ai.execution.started",

            AiExecutionCompletedDomainEvent => "ai.execution.completed",

            AiExecutionFailedDomainEvent => "ai.execution.failed",

            AiExecutionCancelledDomainEvent => "ai.execution.cancelled",

            AiExecutionFallbackUsedDomainEvent => "ai.execution.fallback-used",

            _ => $"ai.{domainEvent.GetType().Name}",
        };
    }

    public static string GetEventType(IDomainEvent domainEvent)
    {
        return domainEvent.GetType().FullName ?? domainEvent.GetType().Name;
    }
}
