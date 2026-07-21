using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Executions.Events;

public sealed record AiExecutionFailedDomainEvent(
    Guid id,
    string errorCode,
    string errorMessage,
    long durationMilliseconds
) : DomainEvent;
