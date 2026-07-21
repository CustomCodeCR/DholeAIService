using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Executions.Events;

public sealed record AiExecutionFallbackUsedDomainEvent(
    Guid id,
    Guid previousModelId,
    Guid nextModelId,
    string reason
) : DomainEvent;
