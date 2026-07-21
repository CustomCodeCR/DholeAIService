namespace Dhole.AI.Application.Abstractions.Auditing;

public sealed record AiAuditEvent(
    string EventType,
    string Action,
    string EntityType,
    Guid? EntityId = null,
    Guid? ActorUserId = null,
    string? ActorUserName = null,
    object? Before = null,
    object? After = null,
    object? Payload = null,
    object? Metadata = null,
    string? ErrorMessage = null,
    Guid? EventId = null,
    DateTime? OccurredAt = null
);
