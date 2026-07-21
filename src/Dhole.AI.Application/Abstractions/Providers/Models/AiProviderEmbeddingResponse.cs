namespace Dhole.AI.Application.Abstractions.Providers.Models;

public sealed record AiProviderEmbeddingResponse(
    IReadOnlyCollection<IReadOnlyCollection<float>> Embeddings,
    int Dimensions,
    int InputTokens,
    string? RawResponseJson = null
);
