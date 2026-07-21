
using Dhole.AI.Application.Abstractions.Cache;

namespace Dhole.AI.Worker.Streams;

internal sealed class AiModelCreatedStreamHandler(
    IAiModelCacheService cache,
    ILogger<AiModelCreatedStreamHandler> logger
) : AiModelCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.model.created";
}

internal sealed class AiModelUpdatedStreamHandler(
    IAiModelCacheService cache,
    ILogger<AiModelUpdatedStreamHandler> logger
) : AiModelCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.model.updated";
}

internal sealed class AiModelDeletedStreamHandler(
    IAiModelCacheService cache,
    ILogger<AiModelDeletedStreamHandler> logger
) : AiModelCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.model.deleted";
}

internal sealed class AiModelActivatedStreamHandler(
    IAiModelCacheService cache,
    ILogger<AiModelActivatedStreamHandler> logger
) : AiModelCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.model.activated";
}

internal sealed class AiModelInactivatedStreamHandler(
    IAiModelCacheService cache,
    ILogger<AiModelInactivatedStreamHandler> logger
) : AiModelCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.model.inactivated";
}
