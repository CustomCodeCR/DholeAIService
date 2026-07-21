
using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.AI.Application.Abstractions.Cache;

namespace Dhole.AI.Worker.Streams;

internal abstract class AiPromptTemplateCacheInvalidationStreamHandlerBase(
    IAiPromptTemplateCacheService cache,
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
            var promptTemplateId = AiStreamPayloadReader.TryGetGuid(
                document.RootElement,
                "promptTemplateId",
                "entityId",
                "id"
            );

            if (!promptTemplateId.HasValue)
            {
                throw new InvalidOperationException(
                    $"El evento '{envelope.MessageType}' no contiene un identificador de plantilla válido."
                );
            }

            await cache.RemovePromptTemplateCacheAsync(
                promptTemplateId.Value,
                cancellationToken
            );

            logger.LogInformation(
                "AI prompt template cache invalidated. MessageType: {MessageType}, MessageId: {MessageId}, PromptTemplateId: {PromptTemplateId}.",
                envelope.MessageType,
                envelope.MessageId,
                promptTemplateId.Value
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to invalidate AI prompt template cache. MessageType: {MessageType}, MessageId: {MessageId}.",
                envelope.MessageType,
                envelope.MessageId
            );
            throw;
        }
    }
}
