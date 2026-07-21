
using Dhole.AI.Application.Abstractions.Cache;

namespace Dhole.AI.Worker.Streams;

internal sealed class AiPromptTemplateCreatedStreamHandler(
    IAiPromptTemplateCacheService cache,
    ILogger<AiPromptTemplateCreatedStreamHandler> logger
) : AiPromptTemplateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.prompt-template.created";
}

internal sealed class AiPromptTemplateUpdatedStreamHandler(
    IAiPromptTemplateCacheService cache,
    ILogger<AiPromptTemplateUpdatedStreamHandler> logger
) : AiPromptTemplateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.prompt-template.updated";
}

internal sealed class AiPromptTemplateDeletedStreamHandler(
    IAiPromptTemplateCacheService cache,
    ILogger<AiPromptTemplateDeletedStreamHandler> logger
) : AiPromptTemplateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.prompt-template.deleted";
}

internal sealed class AiPromptTemplateActivatedStreamHandler(
    IAiPromptTemplateCacheService cache,
    ILogger<AiPromptTemplateActivatedStreamHandler> logger
) : AiPromptTemplateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.prompt-template.activated";
}

internal sealed class AiPromptTemplateInactivatedStreamHandler(
    IAiPromptTemplateCacheService cache,
    ILogger<AiPromptTemplateInactivatedStreamHandler> logger
) : AiPromptTemplateCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "ai.prompt-template.inactivated";
}
