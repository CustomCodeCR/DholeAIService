namespace Dhole.AI.Contracts.Executions.Request;

public sealed record ExecuteAiChatRequest(
    string ProfileKey,
    IReadOnlyCollection<AiMessageRequest> Messages,
    IReadOnlyCollection<AiPromptVariableRequest>? Variables = null,
    string? CorrelationId = null,
    string? RequestHash = null
);
