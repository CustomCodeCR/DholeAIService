namespace Dhole.AI.Contracts.Profiles.Request;

public sealed record ConfigureAiProfileModelsRequest(
    IReadOnlyCollection<AiProfileModelRequest> Models
);
