using CustomCodeFramework.Core.Domain.Events;
using Dhole.AI.Domain.Executions.Enums;

namespace Dhole.AI.Domain.Executions.Events;

public sealed record AiExecutionCompletedDomainEvent(
    Guid id,
    Guid? connectionId,
    Guid? modelId,
    int inputTokens,
    int outputTokens,
    decimal estimatedCost,
    long durationMilliseconds,
    AiFinishReason finishReason
) : DomainEvent;
