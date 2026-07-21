namespace Dhole.AI.Application.Abstractions.Providers.Models;

public sealed record AiProviderChatResponse(
    string Content,
    int InputTokens,
    int OutputTokens,
    string FinishReason,
    string? RawResponseJson = null
);
