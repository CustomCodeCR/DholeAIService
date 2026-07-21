using Dhole.AI.Domain.Models.Enums;

namespace Dhole.AI.Application.Abstractions.Providers.Models;

public sealed record AiDiscoveredModel(
    string ExternalModelId,
    string Name,
    AiModelCapability Capabilities,
    int? ContextWindow,
    int? MaximumOutputTokens,
    bool IsLocal
);
