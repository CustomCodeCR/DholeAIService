using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.AI.Domain.Executions.Events;

public sealed record AiExecutionCancelledDomainEvent(Guid id, string? reason, Guid? cancelledBy)
    : DomainEvent;
