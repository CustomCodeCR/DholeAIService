
using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.AI.Application.Abstractions.Cache;

namespace Dhole.AI.Worker.Streams;

internal abstract class AiProfileCacheInvalidationStreamHandlerBase(
    IAiProfileCacheService cache,
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
            var profileId = AiStreamPayloadReader.TryGetGuid(
                document.RootElement,
                "profileId",
                "entityId",
                "id"
            );
            var key = AiStreamPayloadReader.TryGetString(document.RootElement, "key", "profileKey");

            if (!profileId.HasValue)
            {
                throw new InvalidOperationException(
                    $"El evento '{envelope.MessageType}' no contiene un identificador de perfil válido."
                );
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                await cache.RemoveProfileCacheAsync(profileId.Value, key, cancellationToken);
            }
            else
            {
                await cache.RemoveByIdAsync(profileId.Value, cancellationToken);
            }

            logger.LogInformation(
                "AI profile cache invalidated. MessageType: {MessageType}, MessageId: {MessageId}, ProfileId: {ProfileId}.",
                envelope.MessageType,
                envelope.MessageId,
                profileId.Value
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to invalidate AI profile cache. MessageType: {MessageType}, MessageId: {MessageId}.",
                envelope.MessageType,
                envelope.MessageId
            );
            throw;
        }
    }
}
