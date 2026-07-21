using CustomCodeFramework.Core.Domain.Events;
using Dhole.AI.Domain.Executions.Enums;

namespace Dhole.AI.Domain.Executions.Events;

public sealed record AiExecutionStartedDomainEvent(
    Guid id,
    Guid profileId,
    string profileKey,
    AiExecutionType executionType,
    Guid? requestedBy
) : DomainEvent;
