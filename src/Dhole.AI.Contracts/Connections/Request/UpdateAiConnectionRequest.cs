namespace Dhole.AI.Contracts.Connections.Request;

public sealed record UpdateAiConnectionRequest(
    string Name,
    string ProviderType,
    string BaseUrl,
    string? SecretReference,
    int TimeoutSeconds
);
