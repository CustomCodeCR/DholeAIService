namespace Dhole.AI.Application.Abstractions.Providers.Models;

public sealed record AiProviderEmbeddingRequest(IReadOnlyCollection<string> Inputs);
