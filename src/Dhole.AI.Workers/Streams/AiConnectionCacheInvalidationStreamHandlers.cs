
using Dhole.AI.Application.Abstractions.Cache;

namespace Dhole.AI.Worker.Streams;

internal sealed class AiConnectionCreatedStreamHandler(
    IAiConnectionCacheService cache,
    ILogger<AiConnectionCreatedStreamHandler> logger
) : AiConnectionCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.connection.created";
}

internal sealed class AiConnectionUpdatedStreamHandler(
    IAiConnectionCacheService cache,
    ILogger<AiConnectionUpdatedStreamHandler> logger
) : AiConnectionCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.connection.updated";
}

internal sealed class AiConnectionDeletedStreamHandler(
    IAiConnectionCacheService cache,
    ILogger<AiConnectionDeletedStreamHandler> logger
) : AiConnectionCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.connection.deleted";
}

internal sealed class AiConnectionActivatedStreamHandler(
    IAiConnectionCacheService cache,
    ILogger<AiConnectionActivatedStreamHandler> logger
) : AiConnectionCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.connection.activated";
}

internal sealed class AiConnectionInactivatedStreamHandler(
    IAiConnectionCacheService cache,
    ILogger<AiConnectionInactivatedStreamHandler> logger
) : AiConnectionCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.connection.inactivated";
}
