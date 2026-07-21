namespace Dhole.AI.Application.Abstractions.Messaging;

public interface IIntegrationEventOutboxWriter
{
    Task WriteAsync(
        string eventType,
        string eventName,
        object payload,
        string? correlationId = null,
        CancellationToken cancellationToken = default
    );
}
