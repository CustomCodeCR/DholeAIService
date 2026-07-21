namespace Dhole.AI.Application.Abstractions.Mongo;

public interface IAiConfigurationSnapshotWriter
{
    Task WriteAsync(
        Guid eventId,
        string eventName,
        string entityType,
        Guid entityId,
        string action,
        object? previousValue,
        object? newValue,
        Guid? changedBy,
        DateTime changedAtUtc,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default
    );
}
