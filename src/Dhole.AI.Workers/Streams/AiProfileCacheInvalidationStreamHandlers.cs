
using Dhole.AI.Application.Abstractions.Cache;

namespace Dhole.AI.Worker.Streams;

internal sealed class AiProfileCreatedStreamHandler(
    IAiProfileCacheService cache,
    ILogger<AiProfileCreatedStreamHandler> logger
) : AiProfileCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.profile.created";
}

internal sealed class AiProfileUpdatedStreamHandler(
    IAiProfileCacheService cache,
    ILogger<AiProfileUpdatedStreamHandler> logger
) : AiProfileCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.profile.updated";
}

internal sealed class AiProfileDeletedStreamHandler(
    IAiProfileCacheService cache,
    ILogger<AiProfileDeletedStreamHandler> logger
) : AiProfileCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.profile.deleted";
}

internal sealed class AiProfileActivatedStreamHandler(
    IAiProfileCacheService cache,
    ILogger<AiProfileActivatedStreamHandler> logger
) : AiProfileCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.profile.activated";
}

internal sealed class AiProfileInactivatedStreamHandler(
    IAiProfileCacheService cache,
    ILogger<AiProfileInactivatedStreamHandler> logger
) : AiProfileCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.profile.inactivated";
}

internal sealed class AiProfileModelsChangedStreamHandler(
    IAiProfileCacheService cache,
    ILogger<AiProfileModelsChangedStreamHandler> logger
) : AiProfileCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.profile.models-changed";
}
