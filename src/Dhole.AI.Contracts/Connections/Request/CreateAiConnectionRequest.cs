namespace Dhole.AI.Contracts.Connections.Request;

public sealed record CreateAiConnectionRequest(
    string Name,
    string ProviderType,
    string BaseUrl,
    string? SecretReference,
    int TimeoutSeconds = 120
);
