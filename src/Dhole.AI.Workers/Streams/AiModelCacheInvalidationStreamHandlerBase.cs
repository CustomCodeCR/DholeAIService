
using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.AI.Application.Abstractions.Cache;

namespace Dhole.AI.Worker.Streams;

internal abstract class AiModelCacheInvalidationStreamHandlerBase(
    IAiModelCacheService cache,
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
            var modelId = AiStreamPayloadReader.TryGetGuid(
                document.RootElement,
                "modelId",
                "entityId",
                "id"
            );
            var connectionId = AiStreamPayloadReader.TryGetGuid(
                document.RootElement,
                "connectionId"
            );

            if (!modelId.HasValue)
            {
                throw new InvalidOperationException(
                    $"El evento '{envelope.MessageType}' no contiene un identificador de modelo válido."
                );
            }

            if (connectionId.HasValue)
            {
                await cache.RemoveModelCacheAsync(
                    modelId.Value,
                    connectionId.Value,
                    cancellationToken
                );
            }
            else
            {
                await cache.RemoveByIdAsync(modelId.Value, cancellationToken);
            }

            logger.LogInformation(
                "AI model cache invalidated. MessageType: {MessageType}, MessageId: {MessageId}, ModelId: {ModelId}.",
                envelope.MessageType,
                envelope.MessageId,
                modelId.Value
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to invalidate AI model cache. MessageType: {MessageType}, MessageId: {MessageId}.",
                envelope.MessageType,
                envelope.MessageId
            );
            throw;
        }
    }
}
