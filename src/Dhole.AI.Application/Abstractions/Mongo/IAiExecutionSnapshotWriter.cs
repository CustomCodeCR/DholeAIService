namespace Dhole.AI.Application.Abstractions.Mongo;

public interface IAiExecutionSnapshotWriter
{
    Task WriteAsync(
        Guid executionId,
        string profileKey,
        string executionType,
        string status,
        object? request,
        object? response,
        object? providerMetadata,
        string? errorCode,
        string? errorMessage,
        DateTime occurredAtUtc,
        string? correlationId = null,
        CancellationToken cancellationToken = default
    );
}
