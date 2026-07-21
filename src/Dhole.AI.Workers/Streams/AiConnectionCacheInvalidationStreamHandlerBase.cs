
using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.AI.Application.Abstractions.Cache;

namespace Dhole.AI.Worker.Streams;

internal abstract class AiConnectionCacheInvalidationStreamHandlerBase(
    IAiConnectionCacheService cache,
    ILogger logger
) : IRedisStreamMessageHandler
{
    public abstract string MessageType { get; }

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var document = JsonDocument.Parse(envelope.PayloadJson);
            var connectionId = AiStreamPayloadReader.TryGetGuid(
                document.RootElement,
                "connectionId",
                "entityId",
                "id"
            );

            if (!connectionId.HasValue)
            {
                throw new InvalidOperationException(
                    $"El evento '{envelope.MessageType}' no contiene un identificador de conexión válido."
                );
            }

            await cache.RemoveConnectionCacheAsync(connectionId.Value, cancellationToken);

            logger.LogInformation(
                "AI connection cache invalidated. MessageType: {MessageType}, MessageId: {MessageId}, ConnectionId: {ConnectionId}.",
                envelope.MessageType,
                envelope.MessageId,
                connectionId.Value
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to invalidate AI connection cache. MessageType: {MessageType}, MessageId: {MessageId}.",
                envelope.MessageType,
                envelope.MessageId
            );
            throw;
        }
    }
}
