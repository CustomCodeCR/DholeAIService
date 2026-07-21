using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Application.Abstractions.Providers.Models;

public sealed record AiProviderContext(
    Guid ConnectionId,
    string ConnectionName,
    AiProviderType ProviderType,
    string BaseUrl,
    string? Secret,
    int TimeoutSeconds,
    Guid? ModelId = null,
    string? ModelName = null,
    string? ExternalModelId = null,
    AiModelCapability Capabilities = AiModelCapability.None
);
