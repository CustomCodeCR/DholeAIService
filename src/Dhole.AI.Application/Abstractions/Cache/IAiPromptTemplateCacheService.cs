using Dhole.AI.Contracts.PromptTemplates.Response;

namespace Dhole.AI.Application.Abstractions.Cache;

public interface IAiPromptTemplateCacheService
{
    Task<AiPromptTemplateDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetByIdAsync(
        Guid id,
        AiPromptTemplateDto template,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task RemovePromptTemplateCacheAsync(Guid id, CancellationToken cancellationToken = default);
}
