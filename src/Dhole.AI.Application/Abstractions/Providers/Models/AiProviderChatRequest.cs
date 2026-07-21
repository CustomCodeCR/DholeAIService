namespace Dhole.AI.Application.Abstractions.Providers.Models;

public sealed record AiProviderChatRequest(
    IReadOnlyCollection<AiProviderMessage> Messages,
    decimal Temperature,
    int MaximumOutputTokens,
    bool RequiresStructuredOutput,
    string? JsonSchema
);
